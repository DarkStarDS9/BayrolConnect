using System.Text.Json;
using BayrolLib;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Prometheus;

namespace BayrolConnect;

public static class Program
{
    private static ILogger _logger = null!;
    private static BayrolMqttConnector? _mqttConnector;

    public static async Task Main(string[] args)
    {
        var configJson = args.Length == 1 ? args[0] : Environment.GetEnvironmentVariable("CONFIG");

        if(string.IsNullOrEmpty(configJson))
        {
            throw new InvalidOperationException("Configuration not provided");
        }

        var config = JsonSerializer.Deserialize<Configuration>(configJson) ??
                     throw new InvalidOperationException("Failed to deserialize configuration");

        var loggerBuilder = LoggerFactory.Create(builder =>
            builder.AddSimpleConsole(options =>
                {
                    options.IncludeScopes = true;
                    options.SingleLine = true;
                    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fff] ";
                })
                .SetMinimumLevel(config.LogLevel));

        _logger = config.UseMqtt
            ? loggerBuilder.CreateLogger<BayrolMqttConnector>()
            : loggerBuilder.CreateLogger<BayrolWebConnector>();

        // Start HTTP server: Prometheus metrics + production control API on port 8080
        var webBuilder = WebApplication.CreateBuilder(new[] { "--urls", "http://0.0.0.0:8080" });
        webBuilder.Logging.ClearProviders();  // suppress ASP.NET Core startup noise
        var webApp = webBuilder.Build();

        webApp.MapMetrics();  // Prometheus at /metrics

        webApp.MapPost("/api/production/start", async (ProductionStartRequest req) =>
        {
            if (_mqttConnector == null)
                return Results.Problem("MQTT connector not initialized", statusCode: 503);
            await _mqttConnector.StartManualProduction(req.PowerPct, req.RuntimeMinutes, req.OrpShutoffMv);
            return Results.Ok(new { scheduled = true, req.PowerPct, req.RuntimeMinutes, req.OrpShutoffMv });
        });

        webApp.MapDelete("/api/production", async () =>
        {
            if (_mqttConnector == null)
                return Results.Problem("MQTT connector not initialized", statusCode: 503);
            await _mqttConnector.StopManualProduction();
            return Results.Ok(new { stopped = true });
        });

        await webApp.StartAsync();

        if (config.UseMqtt)
        {
            await ConnectUsingMqttAsync(config);
        }
        else
        {
            await ConnectUsingWebAsync(config);
        }
    }

    private static async Task ConnectUsingWebAsync(Configuration config)
    {
        var connector = new BayrolWebConnector(config.User, config.Password, _logger, TimeProvider.System);

        while (true)
        {
            var values = await connector.GetDeviceDataAsync(config.Cid);

            if (values.DeviceState != DeviceState.Error)
            {
                UpdateMetrics(values);
            }
            else
            {
                _logger.LogWarning($"Device is not OK: {values.ErrorMessage}");
            }
            
            await Task.Delay(GetNextIntervalDelay());
        }
    }
    
    private static async Task ConnectUsingMqttAsync(Configuration config)
    {
        var sortedTargetValues = config.RedoxTargetValues?.OrderBy(kv => kv.Key).ToList();
        var connector = new BayrolMqttConnector(
            config.User, config.Password, config.Cid, _logger, TimeProvider.System,
            repollInterval: config.RepollIntervalSeconds is { } repoll ? TimeSpan.FromSeconds(repoll) : null,
            staleWarnThreshold: config.StaleWarnSeconds is { } warn ? TimeSpan.FromSeconds(warn) : null,
            staleReconnectThreshold: config.StaleReconnectSeconds is { } reconnect ? TimeSpan.FromSeconds(reconnect) : null);
        _mqttConnector = connector;
        await connector.ConnectAsync();

        var lastState = DeviceState.Offline;

        var isInStartup = true;
        
        ExtendedAutomaticSaltDeviceData? values = null;
        
        while (true)
        {
            do
            {
                values = connector.GetDeviceData();
            } while (isInStartup && values.DeviceState == DeviceState.Offline && await StartupRetryDelayAsync());

            isInStartup = false;

            if(values.DeviceState != lastState)
            {
                _logger.LogInformation($"Device state changed: {values.DeviceState}");
                lastState = values.DeviceState;
            }

            if (values.DeviceState != DeviceState.Offline)
            {
                try
                {
                    var newRedoxTarget = GetNewRedoxTarget(sortedTargetValues, values.RedoxTarget);
                    if (newRedoxTarget != null)
                    {
                        await connector.SetRedoxTarget(newRedoxTarget.Value);
                        _logger.LogInformation($"New Redox Target: {newRedoxTarget}");
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning($"Error settings new redox target: {e}");
                }

                UpdateMetrics(values);
            }
            else
            {
                _logger.LogWarning("Device is offline");
            }
        
            await Task.Delay(GetNextIntervalDelay());
        }        
    }

    private static async ValueTask<bool> StartupRetryDelayAsync()
    {
        _logger.LogInformation("Device not ready yet, waiting for 5 seconds before retrying...");
        await Task.Delay(TimeSpan.FromSeconds(5));
        return true;
    }

    private static void UpdateMetrics(AutomaticSaltDeviceData data)
    {
        Metrics.DeviceState.Set((int)data.DeviceState);
        Metrics.PhValue.Set((double)data.Ph);
        Metrics.RedoxValue.Set(data.Redox);
        Metrics.TemperatureValue.Set((double)data.Temperature);
        Metrics.SaltValue.Set((double)data.Salt);

        if (data is ExtendedAutomaticSaltDeviceData extendedData)
        {
            Metrics.RedoxTargetValue.Set(extendedData.RedoxTarget);
            Metrics.FilterPumpState.Set(extendedData.FilterPumpState ? 1 : 0);
            Metrics.PhAutomationState.Set(extendedData.PhAutomationState ? 1 : 0);
            Metrics.SaltProductionState.Set(extendedData.SaltProductionState ? 1 : 0);
            Metrics.BoostModeState.Set(extendedData.BoostModeState ? 1 : 0);
            Metrics.PhDosingRate.Set(extendedData.PhDosingRate);
            Metrics.SaltProductionRate.Set(extendedData.SaltProductionRate);
            Metrics.CanisterState.Set(extendedData.CanisterState ? 1 : 0);
            Metrics.WeightedOpTimeHours.Set(extendedData.WeightedOpTimeMinutes / 60.0);
            Metrics.SeManualActive.Set(extendedData.SeManualActive ? 1 : 0);
            Metrics.SeManualProgressMinutes.Set(extendedData.SeManualProgressMinutes);
        }
    }

    /// <summary>
    /// If there should be a new target value, returns this new value, otherwise returns null.
    /// </summary>
    /// <param name="sortedTargetValues">the list of time-based target values</param>
    /// <param name="currentRedoxTargetValue">the current redox value</param>
    /// <param name="timeProvider"></param>
    /// <returns>new target value or <b>null</b> if no change is necessary</returns>
    /// <exception cref="NotImplementedException"></exception>
    internal static int? GetNewRedoxTarget(List<KeyValuePair<TimeSpan, int>>? sortedTargetValues, int currentRedoxTargetValue, TimeProvider? timeProvider = null)
    {
        if (sortedTargetValues == null || sortedTargetValues.Count == 0) return null;
        
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        
        var targetValue = sortedTargetValues
            .OrderBy(kv => kv.Key)
            .Where(kv => now.TimeOfDay >= kv.Key)
            .Select(kv => (int?)kv.Value)
            .LastOrDefault();

        var newTarget = targetValue ?? sortedTargetValues.Last().Value;
        
        return newTarget == currentRedoxTargetValue ? null : newTarget;
    }

    static TimeSpan GetNextIntervalDelay()
    {
        var now = DateTime.Now;
        var minutes = now.Minute % 5;
        var secondsToNextInterval = (5 - minutes) * 60 - now.Second;
        return TimeSpan.FromSeconds(secondsToNextInterval);
    }
}

record ProductionStartRequest(int PowerPct, int RuntimeMinutes, int OrpShutoffMv);