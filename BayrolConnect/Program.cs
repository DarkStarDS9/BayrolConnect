using System.Text.Json;
using BayrolLib;
using Microsoft.Extensions.Logging;

namespace BayrolConnect;

public static class Program
{
    private static ILogger _logger = null!;
    
    public static async Task Main(string[] args)
    {
        var configJson = args.Length == 1 ? args[0] : Environment.GetEnvironmentVariable("CONFIG");
       
        if(string.IsNullOrEmpty(configJson))
        {
            throw new InvalidOperationException("Configuration not provided");
        }

        var config = JsonSerializer.Deserialize<Configuration>(configJson) ??
                     throw new InvalidOperationException("Failed to deserialize configuration");

        var loggerBuilder = LoggerFactory.Create(builder => builder.AddConsole()
            .SetMinimumLevel(config.LogLevel));
        
        _logger = config.UseMqtt
            ? loggerBuilder.CreateLogger<BayrolMqttConnector>()
            : loggerBuilder.CreateLogger<BayrolWebConnector>();

        await using var azureDevice = new AzureIotCentralDevice(config.IdScope, config.DeviceId, config.PrimaryKey, _logger);
        if(!await azureDevice.Connect()) return;
        
        if (config.UseMqtt)
        {
            await ConnectUsingMqtt(config, azureDevice);
        }
        else
        {
            await ConnectUsingWeb(config, azureDevice);            
        }
    }

    private static async Task ConnectUsingWeb(Configuration config, AzureIotCentralDevice azureDevice)
    {
        var connector = new BayrolWebConnector(config.User, config.Password, _logger, TimeProvider.System);

        while (true)
        {
            try
            {
                var values = await connector.GetDeviceDataAsync(config.Cid);

                if (values.DeviceState != DeviceState.Error)
                {
                    var messageString = JsonSerializer.Serialize(values);
                    await azureDevice.SendEventAsync(messageString);
                }
                else
                {
                    _logger.LogWarning($"Device is not OK: {values.ErrorMessage}");
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to send device data");
            }
            
            await Task.Delay(GetNextIntervalDelay());
        }
    }
    
    private static async Task ConnectUsingMqtt(Configuration config, AzureIotCentralDevice azureDevice)
    {
        var connector = new BayrolMqttConnector(config.User, config.Password, config.Cid, _logger, TimeProvider.System);
        await connector.ConnectAsync();
        
        while (true)
        {
            try
            {
                var values = connector.GetDeviceData();

                if (values.DeviceState != DeviceState.Error)
                {
                    var messageString = JsonSerializer.Serialize(values);
                    await azureDevice.SendEventAsync(messageString);
                }
                else
                {
                    _logger.LogWarning($"Device is not OK: {values.ErrorMessage}");
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to send device data");
            }
            
            await Task.Delay(GetNextIntervalDelay());
        }        
    }

    static TimeSpan GetNextIntervalDelay()
    {
        var now = DateTime.Now;
        var minutes = now.Minute % 5;
        var secondsToNextInterval = (5 - minutes) * 60 - now.Second;
        return TimeSpan.FromSeconds(secondsToNextInterval);
    }    
}