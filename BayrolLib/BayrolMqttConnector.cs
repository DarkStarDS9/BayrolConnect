using System.Text.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Internal; // For MqttNetLog
using MQTTnet.Diagnostics; // For MqttNetLogLevel

namespace BayrolLib;

public class BayrolMqttConnector(
    string username,
    string password,
    string cid,
    ILogger logger, // This is the logger passed in
    TimeProvider timeProvider)
{
    private static bool _mqttNetLoggingSubscribed = false;
    private static readonly object _logSubscriptionLock = new object();

    private const string MqttServer = "wss://www.bayrol-poolaccess.de:8083";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Ensure MQTTnet logging is subscribed to using the logger from the first instance.
    private readonly ILogger _instanceLogger = SubscribeToMqttNetLogs(logger);
    
    private readonly BayrolWebConnector _webConnector = new(username, password, logger, timeProvider);

    private readonly ExtendedAutomaticSaltDeviceData _deviceData = new()
    {
        DeviceState = DeviceState.Offline,
        ErrorMessage = "Not connected to MQTT server yet",
        ObtainedAt = timeProvider.GetUtcNow()
    };
    
    // private readonly ILogger _logger = InitMqttLogger(logger); // Removed, logger is used directly

    // private static ILogger InitMqttLogger(ILogger loggerInstance) // Removed
    // {
    //     lock (_loggerLock)
    //     {
    //         if (!_mqttNetLoggerInitialized)
    //         {
    //             MqttNetGlobalLogger.Adapter = new MqttNetLoggerAdapter(loggerInstance);
    //             _mqttNetLoggerInitialized = true;
    //             loggerInstance.LogInformation("MQTTnet global logger initialized via BayrolMqttConnector.");
    //         }
    //     }
    //     return loggerInstance;
    // }

    private BayrolWebConnector.MqttSessionIdResponse? _sessionIdResponse;
    private string? _prefix;
    private readonly HashSet<string> _uninitializedTopics = [];
    private IMqttClient? _client;
    private int _reconnectAttempts = 0;

    private static ILogger SubscribeToMqttNetLogs(ILogger loggerInstance)
    {
        lock (_logSubscriptionLock)
        {
            if (!_mqttNetLoggingSubscribed)
            {
                MqttNetLog.LogMessagePublished += (s, e) =>
                {
                    var msLogLevel = e.Level switch
                    {
                        MQTTnet.Diagnostics.MqttNetLogLevel.Verbose => LogLevel.Trace,
                        MQTTnet.Diagnostics.MqttNetLogLevel.Info    => LogLevel.Information,
                        MQTTnet.Diagnostics.MqttNetLogLevel.Warning => LogLevel.Warning,
                        MQTTnet.Diagnostics.MqttNetLogLevel.Error   => LogLevel.Error,
                        _                       => LogLevel.Debug // Default for anything else
                    };

                    // Use the loggerInstance captured by this lambda
                    loggerInstance.Log(msLogLevel, e.Exception, $"[MQTTnet::{e.Source}] {e.Message}");
                };
                _mqttNetLoggingSubscribed = true;
                loggerInstance.LogInformation("Subscribed to MqttNetLog.LogMessagePublished for detailed MQTT library logging.");
            }
        }
        return loggerInstance; // Return the logger to be assigned to _instanceLogger, or just use logger directly.
    }
    private readonly TimeSpan _initialReconnectDelay = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _maxReconnectDelay = TimeSpan.FromMinutes(5);


    public async Task ConnectAsync()
    {
        do
        {
            _sessionIdResponse = await _webConnector.GetMqttSessionIdAsync(cid);
            if (_sessionIdResponse == null)
            {
                logger.LogWarning("Failed to get session ID, retrying in 5 minutes");
                await Task.Delay(TimeSpan.FromMinutes(5));
            }
            
        } while (_sessionIdResponse == null);

        _prefix =  $"d02/{_sessionIdResponse.DeviceSerial}";
        
        _client = new MqttFactory().CreateMqttClient();
        var options = new MqttClientOptionsBuilder()
            .WithWebSocketServer(o => o.WithUri(MqttServer))
            .WithCredentials(_sessionIdResponse.AccessToken, "*")
            // Configure Keep Alive period. The client will send a PINGREQ if no other packet is sent within this interval.
            // The broker is expected to close the connection if it doesn't receive any packet (control or data)
            // within 1.5 * KeepAlivePeriod. This helps in detecting dead connections.
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
            .Build();

        _client.ApplicationMessageReceivedAsync += ClientOnApplicationMessageReceivedAsync;
        _client.DisconnectedAsync += ClientOnDisconnectedAsync;
        
        var response = await _client.ConnectAsync(options, CancellationToken.None);
        
        if(response.ResultCode != MqttClientConnectResultCode.Success)
        {
            // If connection fails, an exception is thrown, and the caller (Program.cs) might terminate or handle it.
            // If ClientOnDisconnectedAsync calls ConnectAsync and it fails, this exception will be caught by the retry logic there.
            throw new InvalidOperationException($"Failed to connect to MQTT server: {response.ResultCode}");
        }

        // Reset reconnect attempts on successful connection
        _reconnectAttempts = 0;
        logger.LogInformation("Successfully connected to MQTT server.");

        foreach (var topic in MqttMapping.AllTopics)
        {
            var fullTopic = $"{_prefix}/v/{topic}";
            _uninitializedTopics.Add(fullTopic);
            var subscribeResult = await _client.SubscribeAsync(fullTopic);
            var publishResult = await _client.PublishStringAsync($"{_prefix}/g/{topic}");

            logger.LogInformation($"Subscribed to {subscribeResult.Items.First().TopicFilter.Topic}: {subscribeResult.ReasonString}, data-request-result: {publishResult.ReasonCode}");
        }
    }

    private async Task ClientOnDisconnectedAsync(MqttClientDisconnectedEventArgs arg)
    {
        logger.LogWarning($"Disconnected from MQTT server. Reason: {arg.ReasonString}. ClientWasConnected: {arg.ClientWasConnected}. Attempting to reconnect...");

        lock (_deviceData)
        {
            _deviceData.DeviceState = DeviceState.Offline;
            _deviceData.ErrorMessage = "Disconnected from MQTT server, attempting to reconnect.";
            _deviceData.ObtainedAt = timeProvider.GetUtcNow();
        }
        
        _reconnectAttempts++;
        // Implement exponential backoff for reconnection attempts.
        // Delay = InitialDelay * 2^(Attempts-1), capped at MaxDelay.
        var delaySeconds = _initialReconnectDelay.TotalSeconds * Math.Pow(2, _reconnectAttempts - 1);
        var reconnectDelay = TimeSpan.FromSeconds(Math.Min(delaySeconds, _maxReconnectDelay.TotalSeconds));

        logger.LogInformation($"Reconnect attempt {_reconnectAttempts}. Waiting for {reconnectDelay.TotalSeconds} seconds before trying again.");
        await Task.Delay(reconnectDelay);

        try
        {
            // ConnectAsync will reset _reconnectAttempts on successful connection.
            await ConnectAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to reconnect after {_reconnectAttempts} attempts. Will retry after next calculated delay from ClientOnDisconnectedAsync if another disconnect event occurs, or if this was the initial connect, the application might terminate.");
            // If ConnectAsync fails, it throws an exception.
            // The MqttClient's DisconnectedAsync event might be triggered again by the library if the failed ConnectAsync attempt itself causes a disconnect state.
            // Or, if ConnectAsync fails and the client remains in a disconnected state without triggering DisconnectedAsync again,
            // we might not automatically retry from here. However, the MQTTnet client itself might have internal retry mechanisms
            // for the initial connection attempt. Given our loop, if ConnectAsync fails, ClientOnDisconnectedAsync will be called again
            // by the MQTT library when it fully registers the disconnection after a failed connect.
        }
    }

    private Task ClientOnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
    {
        // {"t":"4.2","createdAt":"2024-05-24T05:45:07.013Z","max":82,"min":62,"v":72}
        // {"t":"5.29","createdAt":"2024-05-24T05:45:07.293Z","v":"19.96"} // what is state 19.96?
        var payloadString = arg.ApplicationMessage.ConvertPayloadToString();
        var payload = JsonSerializer.Deserialize<Payload>(payloadString, JsonOptions);

        if (payload == null)
        {
            lock (_deviceData)
            {
                _deviceData.DeviceState = DeviceState.Error;
                _deviceData.ObtainedAt = timeProvider.GetUtcNow();
            }
            throw new Exception("Failed to deserialize payload");
        }

        lock (_deviceData)
        {
            _uninitializedTopics.Remove(arg.ApplicationMessage.Topic);
            
            switch (arg.ApplicationMessage.Topic.Split('/').Last())
            {
                case MqttMapping.DeviceStatus:
                    _deviceData.DeviceState = MqttMapping.ToDeviceState(payload.V);
                    _deviceData.ErrorMessage = _deviceData.DeviceState == DeviceState.Offline ? "Device is offline" : null;
                    break;
                case MqttMapping.PhValue:
                    _deviceData.Ph = payload.V.GetInt32() / 10m;
                    break;
                case MqttMapping.RedoxTargetValue:
                    _deviceData.RedoxTarget = payload.V.GetInt32();
                    break;
                case MqttMapping.RedoxValue:
                    _deviceData.Redox = payload.V.GetInt32();
                    break;
                case MqttMapping.TemperatureValue:
                    _deviceData.Temperature = payload.V.GetInt32() / 10m;
                    break;
                case MqttMapping.SaltValue:
                    _deviceData.Salt = payload.V.GetInt32() / 10m;
                    break;
                case MqttMapping.FilterPumpState:
                    _deviceData.FilterPumpState = MqttMapping.ToBool(payload.V);
                    break;
                case MqttMapping.PhAutomationState:
                    _deviceData.PhAutomationState = MqttMapping.ToBool(payload.V);
                    break;
                case MqttMapping.SaltProductionState:
                    _deviceData.SaltProductionState = MqttMapping.ToBool(payload.V);
                    break;
                case MqttMapping.BoostModeState:
                    _deviceData.BoostModeState = MqttMapping.ToBool(payload.V);
                    break;
                case MqttMapping.PhDosingRate:
                    _deviceData.PhDosingRate = payload.V.GetInt32();
                    break;
                case MqttMapping.SaltProductionRate:
                    _deviceData.SaltProductionRate = payload.V.GetInt32();
                    break;
                case MqttMapping.CanisterState:
                    _deviceData.CanisterState = MqttMapping.ToBool(payload.V);
                    break;
                default:
                    logger.LogWarning($"Unknown topic {payload.T} with value {payload.V}");
                    break;
            }

            _deviceData.ObtainedAt = timeProvider.GetUtcNow();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Will set the target Redox value in mV if we are connected to the MQTT server.
    /// Will NOT throw an error if we are not connected yet.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public Task SetRedoxTarget(int value)
        => _client?.PublishStringAsync($"{_prefix}/s/{MqttMapping.RedoxTargetValue}",
               $"{{\"t\":\"{MqttMapping.RedoxTargetValue}\",\"v\":{value},\"min\":400,\"max\":950}}") ??
           Task.CompletedTask;

    public ExtendedAutomaticSaltDeviceData GetDeviceData()
    {
        lock (_deviceData)
        {
            return _uninitializedTopics.Count > 0
                ? new ExtendedAutomaticSaltDeviceData
                {
                    DeviceState = DeviceState.Offline,
                    ErrorMessage = "MQTT retrieving initial values"
                }
                : _deviceData.Clone();
        }
    }

    private class Payload
    {
        public string? T { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? Max { get; set; }
        public int? Min { get; set; }
        public JsonElement V { get; set; }
    }
}