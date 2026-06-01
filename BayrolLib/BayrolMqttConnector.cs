using System.Text.Json;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Diagnostics; // For MqttNetLogLevel
using MQTTnet.Protocol; // For MqttQualityOfServiceLevel

namespace BayrolLib;

public class BayrolMqttConnector(
    string username,
    string password,
    string cid,
    ILogger logger, // This is the logger passed in
    TimeProvider timeProvider,
    TimeSpan? repollInterval = null,
    TimeSpan? staleWarnThreshold = null,
    TimeSpan? staleReconnectThreshold = null)
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

    // The Bayrol server pushes value changes; it does not reliably keep pushing every
    // topic forever (redox / production rate in particular can go silent). To avoid
    // stalled values we actively re-request all values on an interval and watch each
    // topic's freshness, forcing a full reconnect if a topic goes silent for too long.
    private readonly TimeSpan _repollInterval = repollInterval ?? TimeSpan.FromSeconds(60);
    private readonly TimeSpan _staleWarnThreshold = staleWarnThreshold ?? TimeSpan.FromMinutes(3);
    private readonly TimeSpan _staleReconnectThreshold = staleReconnectThreshold ?? TimeSpan.FromMinutes(6);
    private readonly TimeSpan _watchdogInterval = TimeSpan.FromSeconds(30);

    // Last time we received a message for each topic id (e.g. "4.82"). Guarded by _deviceData.
    private readonly Dictionary<string, DateTimeOffset> _lastTopicUpdate = new();
    private bool _backgroundTasksStarted;
    private volatile bool _forcedReconnectInProgress;

    // Pending echo completions for StartManualProduction — guarded by _deviceData lock
    private readonly Dictionary<string, TaskCompletionSource<int>> _pendingEchos = new();

    private static ILogger SubscribeToMqttNetLogs(ILogger loggerInstance)
    {
        lock (_logSubscriptionLock)
        {
            if (!_mqttNetLoggingSubscribed)
            {
                var mqttNetLogger = new MqttNetEventLogger();
                mqttNetLogger.LogMessagePublished += (s, e) =>
                {
                    var msLogLevel = e.LogMessage.Level switch
                    {
                        MQTTnet.Diagnostics.MqttNetLogLevel.Verbose => LogLevel.Trace,
                        MQTTnet.Diagnostics.MqttNetLogLevel.Info    => LogLevel.Information,
                        MQTTnet.Diagnostics.MqttNetLogLevel.Warning => LogLevel.Warning,
                        MQTTnet.Diagnostics.MqttNetLogLevel.Error   => LogLevel.Error,
                        _                       => LogLevel.Debug // Default for anything else
                    };

                    // Use the loggerInstance captured by this lambda
                    loggerInstance.Log(msLogLevel, e.LogMessage.Exception, $"[MQTTnet::{e.LogMessage.Source}] {e.LogMessage.Message}");
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
        _forcedReconnectInProgress = false;
        logger.LogInformation("Successfully connected to MQTT server.");

        var now = timeProvider.GetUtcNow();
        foreach (var topic in MqttMapping.AllTopics)
        {
            var fullTopic = $"{_prefix}/v/{topic}";
            // Subscribe with QoS 1 (at-least-once) so the broker redelivers messages we miss,
            // instead of the default QoS 0 where any dropped update is lost forever.
            var subscribeResult = await _client.SubscribeAsync(fullTopic, MqttQualityOfServiceLevel.AtLeastOnce);

            lock (_deviceData)
            {
                _uninitializedTopics.Add(fullTopic);
                // Seed freshness so a freshly (re)connected session isn't immediately flagged stale.
                _lastTopicUpdate[topic] = now;
            }

            logger.LogInformation($"Subscribed to {subscribeResult.Items.First().TopicFilter.Topic}: {subscribeResult.ReasonString}");
        }

        // Subscribe to control echo topics (4.149, 4.77, 4.150) so StartManualProduction
        // can wait for the device to confirm parameter SETs. These are NOT added to
        // _uninitializedTopics because the device never publishes them proactively.
        foreach (var topic in MqttMapping.ControlEchoTopics)
        {
            var fullTopic = $"{_prefix}/v/{topic}";
            await _client.SubscribeAsync(fullTopic, MqttQualityOfServiceLevel.AtLeastOnce);
            logger.LogInformation($"Subscribed to {fullTopic} (control echo)");
        }

        // Request the current value of every topic now, then keep re-requesting on a timer.
        await RequestAllValuesAsync();

        StartBackgroundTasks();
    }

    /// <summary>
    /// Publishes a getter (<c>/g/</c>) request for every topic so the server resends its
    /// current value. Used both at connect time and periodically to recover from stalled
    /// topics. Never throws - publish failures are logged and ignored.
    /// </summary>
    private async Task RequestAllValuesAsync()
    {
        var client = _client;
        if (client is not { IsConnected: true } || _prefix == null)
        {
            return;
        }

        foreach (var topic in MqttMapping.AllTopics)
        {
            try
            {
                await client.PublishStringAsync($"{_prefix}/g/{topic}",
                    qualityOfServiceLevel: MqttQualityOfServiceLevel.AtLeastOnce);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, $"Failed to request value for topic {topic}");
            }
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

        var topicId = arg.ApplicationMessage.Topic.Split('/').Last();

        lock (_deviceData)
        {
            _uninitializedTopics.Remove(arg.ApplicationMessage.Topic);
            // Only track freshness for topics we know about and (re)seed on connect, so an
            // unexpected/unsubscribed topic can never keep the watchdog reconnecting forever.
            if (_lastTopicUpdate.ContainsKey(topicId))
            {
                _lastTopicUpdate[topicId] = timeProvider.GetUtcNow();
            }

            switch (topicId)
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
                case MqttMapping.SeManualPowerPct:
                case MqttMapping.SeManualRuntime:
                case MqttMapping.SeManualOrpShutoff:
                    // control parameter echoed back by device — used only for echo confirmation
                    break;
                case MqttMapping.SeManualActive:
                    _deviceData.SeManualActive = MqttMapping.ToBool(payload.V);
                    break;
                case MqttMapping.SeManualProgressMin:
                    _deviceData.SeManualProgressMinutes = payload.V.GetInt32();
                    break;
                case MqttMapping.WeightedOpTimeMin:
                    _deviceData.WeightedOpTimeMinutes = payload.V.GetInt32();
                    break;
                default:
                    logger.LogWarning($"Unknown topic {payload.T} with value {payload.V}");
                    break;
            }

            // Complete any pending echo waiter for this topic
            if (_pendingEchos.TryGetValue(topicId, out var echoTcs))
            {
                _pendingEchos.Remove(topicId);
                echoTcs.TrySetResult(payload.V.GetInt32());
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
               $"{{\"t\":\"{MqttMapping.RedoxTargetValue}\",\"v\":{value},\"min\":400,\"max\":950}}",
               MqttQualityOfServiceLevel.AtLeastOnce) ??
           Task.CompletedTask;

    /// <summary>
    /// Starts a timed SE manual production run.
    /// Sends the 4-step activation sequence: power → runtime → ORP shutoff → start.
    /// </summary>
    public async Task StartManualProduction(int powerPct, int runtimeMinutes, int orpShutoffMv)
    {
        if (_client == null) return;

        // The PoolAccess UI waits for the device to echo back runtime and ORP shutoff
        // before sending the start command. Register TCS listeners before sending SETs
        // so we cannot miss the echo.
        var runtimeEcho = RegisterEcho(MqttMapping.SeManualRuntime);
        var shutoffEcho = RegisterEcho(MqttMapping.SeManualOrpShutoff);

        await Publish(MqttMapping.SeManualPowerPct, powerPct);
        await Publish(MqttMapping.SeManualRuntime, runtimeMinutes);
        await Publish(MqttMapping.SeManualOrpShutoff, orpShutoffMv);

        // Wait for device confirmation of both critical parameters (timeout 10s)
        await Task.WhenAll(
            runtimeEcho.WaitAsync(TimeSpan.FromSeconds(10)),
            shutoffEcho.WaitAsync(TimeSpan.FromSeconds(10)));

        await Publish(MqttMapping.SeManualStart, 1);  // function call to start SE manual mode
        logger.LogInformation(
            "SE manual production started: {Power}% for {Minutes}min, ORP shutoff {Orp}mV",
            powerPct, runtimeMinutes, orpShutoffMv);
    }

    private Task<int> RegisterEcho(string topicId)
    {
        var tcs = new TaskCompletionSource<int>();
        lock (_deviceData)
        {
            _pendingEchos[topicId] = tcs;
        }
        return tcs.Task;
    }

    /// <summary>
    /// Stops an active SE manual production run early.
    /// </summary>
    public Task StopManualProduction()
    {
        logger.LogInformation("SE manual production stopped.");
        return PublishEnum(MqttMapping.SeManualStop, 18);  // "19.18" = deactivate
    }

    // Publish a plain integer value (numeric 4.x topics and function call 13.x topics).
    private Task Publish(string topicId, int value)
        => _client?.PublishStringAsync($"{_prefix}/s/{topicId}",
               $"{{\"t\":\"{topicId}\",\"v\":{value}}}") ??
           Task.CompletedTask;

    // Publish an enum value using the "19.XX" compound string format (5.x topics).
    private Task PublishEnum(string topicId, int value)
        => _client?.PublishStringAsync($"{_prefix}/s/{topicId}",
               $"{{\"t\":\"{topicId}\",\"v\":\"19.{value}\"}}") ??
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

    /// <summary>
    /// Starts the periodic re-poll and staleness-watchdog loops exactly once. Reconnects
    /// reuse the same loops, which always read the current <see cref="_client"/> field.
    /// </summary>
    private void StartBackgroundTasks()
    {
        lock (_deviceData)
        {
            if (_backgroundTasksStarted)
            {
                return;
            }
            _backgroundTasksStarted = true;
        }

        _ = Task.Run(RepollLoopAsync);
        _ = Task.Run(WatchdogLoopAsync);
    }

    private async Task RepollLoopAsync()
    {
        using var timer = new PeriodicTimer(_repollInterval);
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                await RequestAllValuesAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error during periodic value re-poll");
            }
        }
    }

    private async Task WatchdogLoopAsync()
    {
        using var timer = new PeriodicTimer(_watchdogInterval);
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                await CheckStalenessAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error during staleness watchdog check");
            }
        }
    }

    private async Task CheckStalenessAsync()
    {
        // If we're not currently connected, a reconnect is already in progress (or about to
        // start) - don't flag everything as stale or pile on another forced reconnect.
        if (_client is not { IsConnected: true } || _forcedReconnectInProgress)
        {
            return;
        }

        List<KeyValuePair<string, TimeSpan>> warn;
        List<KeyValuePair<string, TimeSpan>> reconnect;
        var now = timeProvider.GetUtcNow();

        lock (_deviceData)
        {
            // While initial values are still arriving we don't have a meaningful baseline.
            if (_uninitializedTopics.Count > 0)
            {
                return;
            }

            (warn, reconnect) = EvaluateStaleness(_lastTopicUpdate, now, _staleWarnThreshold, _staleReconnectThreshold);
        }

        foreach (var (topic, age) in warn)
        {
            logger.LogWarning($"Topic {topic} has not updated in {age.TotalSeconds:F0}s (warn threshold {_staleWarnThreshold.TotalSeconds:F0}s).");
        }

        if (reconnect.Count == 0 || _forcedReconnectInProgress)
        {
            return;
        }

        var client = _client;
        if (client == null)
        {
            return;
        }

        _forcedReconnectInProgress = true;
        var topics = string.Join(", ", reconnect.Select(kv => $"{kv.Key} ({kv.Value.TotalSeconds:F0}s)"));
        logger.LogWarning($"Topic(s) stale beyond reconnect threshold ({_staleReconnectThreshold.TotalSeconds:F0}s): {topics}. Forcing reconnect.");

        try
        {
            // Triggers ClientOnDisconnectedAsync, which reconnects and re-subscribes everything.
            await client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to force reconnect from staleness watchdog");
            _forcedReconnectInProgress = false;
        }
    }

    /// <summary>
    /// Pure helper: classifies topics by how long since their last update. Returns the
    /// topics exceeding <paramref name="warnThreshold"/> and those exceeding
    /// <paramref name="reconnectThreshold"/> (a strict superset of warn is not assumed).
    /// </summary>
    internal static (List<KeyValuePair<string, TimeSpan>> warn, List<KeyValuePair<string, TimeSpan>> reconnect) EvaluateStaleness(
        IReadOnlyDictionary<string, DateTimeOffset> lastUpdate,
        DateTimeOffset now,
        TimeSpan warnThreshold,
        TimeSpan reconnectThreshold)
    {
        var warn = new List<KeyValuePair<string, TimeSpan>>();
        var reconnect = new List<KeyValuePair<string, TimeSpan>>();

        foreach (var (topic, last) in lastUpdate)
        {
            var age = now - last;
            if (age > reconnectThreshold)
            {
                reconnect.Add(new KeyValuePair<string, TimeSpan>(topic, age));
            }
            else if (age > warnThreshold)
            {
                warn.Add(new KeyValuePair<string, TimeSpan>(topic, age));
            }
        }

        return (warn, reconnect);
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