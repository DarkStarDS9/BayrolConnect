using System.Text.Json;
using System.Text.Json.Serialization;
using BayrolLib;
using Microsoft.Extensions.Logging;

namespace BayrolConnect;

public static class Program
{
    private static ILogger _logger = null!;
    private static readonly JsonSerializerOptions AzureJsonOptions = new();
    
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

        await using var azureDevice = new AzureIotCentralDevice(config.IdScope, config.DeviceId, config.PrimaryKey, _logger);
        if(!await azureDevice.Connect()) return;

        AzureJsonOptions.Converters.Add(new JsonStringEnumConverter());

        if (config.UseMqtt)
        {
            await ConnectUsingMqttAsync(config, azureDevice);
        }
        else
        {
            await ConnectUsingWebAsync(config, azureDevice);            
        }
    }

    private static async Task ConnectUsingWebAsync(Configuration config, AzureIotCentralDevice azureDevice)
    {
        var connector = new BayrolWebConnector(config.User, config.Password, _logger, TimeProvider.System);

        while (true)
        {
            var values = await connector.GetDeviceDataAsync(config.Cid);

            if (values.DeviceState != DeviceState.Error)
            {
                var messageString = JsonSerializer.Serialize(values, AzureJsonOptions);
                await azureDevice.SendEventAsync(messageString);  // this might throw, but we're not catching it since azureDevice does not recover anyway --> just restart the process
            }
            else
            {
                _logger.LogWarning($"Device is not OK: {values.ErrorMessage}");
            }
            
            await Task.Delay(GetNextIntervalDelay());
        }
    }
    
    private static async Task ConnectUsingMqttAsync(Configuration config, AzureIotCentralDevice azureDevice)
    {
        var connector = new BayrolMqttConnector(config.User, config.Password, config.Cid, _logger, TimeProvider.System);
        await connector.ConnectAsync();
        
        while (true)
        {
            var values = connector.GetDeviceData();

            if (values.DeviceState != DeviceState.Offline)
            {
                var messageString = JsonSerializer.Serialize(values, AzureJsonOptions);
                await azureDevice.SendEventAsync(messageString); // this might throw, but we're not catching it since azureDevice does not recover anyway --> just restart the process
            }
            else
            {
                _logger.LogWarning("Device is offline");
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