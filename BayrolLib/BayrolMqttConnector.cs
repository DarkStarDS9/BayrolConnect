using System.Text.Json;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;

namespace BayrolLib;

public class BayrolMqttConnector(
    string username,
    string password,
    string cid,
    ILogger logger,
    TimeProvider timeProvider)
{
    private const string MqttServer = "wss://www.bayrol-poolaccess.de:8083";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    private readonly BayrolWebConnector _webConnector = new(username, password, logger, timeProvider);

    private readonly ExtendedAutomaticSaltDeviceData _deviceData = new()
    {
        DeviceState = DeviceState.Offline,
        ErrorMessage = "Not connected to MQTT server yet",
        ObtainedAt = timeProvider.GetUtcNow()
    };
    
    private BayrolWebConnector.MqttSessionIdResponse? _sessionIdResponse;
    private string? _prefix;
    private readonly HashSet<string> _uninitializedTopics = [];

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
        
        var client = new MqttFactory().CreateMqttClient();
        var options = new MqttClientOptionsBuilder()
            .WithWebSocketServer(o => o.WithUri(MqttServer))
            .WithCredentials(_sessionIdResponse.AccessToken, "*")
            .Build();

        client.ApplicationMessageReceivedAsync += ClientOnApplicationMessageReceivedAsync;
        client.DisconnectedAsync += ClientOnDisconnectedAsync;
        
        var response = await client.ConnectAsync(options, CancellationToken.None);
        
        if(response.ResultCode != MqttClientConnectResultCode.Success)
        {
            throw new InvalidOperationException($"Failed to connect to MQTT server: {response.ResultCode}");
        }

        foreach (var topic in MqttMapping.AllTopics)
        {
            var fullTopic = $"{_prefix}/v/{topic}";
            _uninitializedTopics.Add(fullTopic);
            var subscribeResult = await client.SubscribeAsync(fullTopic);
            var publishResult = await client.PublishStringAsync($"{_prefix}/g/{topic}");

            logger.LogInformation($"Subscribed to {subscribeResult.Items.First().TopicFilter.Topic}: {subscribeResult.ReasonString}, data-request-result: {publishResult.ReasonCode}");
        }
    }

    private async Task ClientOnDisconnectedAsync(MqttClientDisconnectedEventArgs arg)
    {
        lock (_deviceData)
        {
            _deviceData.DeviceState = DeviceState.Offline;
            _deviceData.ErrorMessage = "Disconnected from MQTT server";
            _deviceData.ObtainedAt = timeProvider.GetUtcNow();
        }
        
        await Task.Delay(TimeSpan.FromSeconds(15));
        await ConnectAsync();
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