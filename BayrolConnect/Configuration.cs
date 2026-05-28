using Microsoft.Extensions.Logging;

namespace BayrolConnect;

public class Configuration
{
    public string User { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string Cid { get; set; } = null!;
    public bool UseMqtt { get; set; } = false;
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
    
    /// <summary>
    /// A list of timespans and the corresponding redox target value in mV.
    /// The list does not need to be sorted.
    /// The values are used to set the redox target value based on the time of day.
    /// For a given time, the last value in the sorted list with a timespan less than or equal to the current time is used.
    /// </summary>
    public List<KeyValuePair<TimeSpan, int>>? RedoxTargetValues { get; set; }

    /// <summary>
    /// (MQTT only) How often, in seconds, to actively re-request all values from the server.
    /// This guards against the server silently stopping pushing some topics (e.g. redox /
    /// production rate). Defaults to 60 seconds when unset.
    /// </summary>
    public int? RepollIntervalSeconds { get; set; }

    /// <summary>
    /// (MQTT only) Log a warning when a topic has not updated for this many seconds.
    /// Defaults to 180 seconds when unset.
    /// </summary>
    public int? StaleWarnSeconds { get; set; }

    /// <summary>
    /// (MQTT only) Force a reconnect (full re-subscribe) when a topic has not updated for
    /// this many seconds despite periodic re-polling. Defaults to 360 seconds when unset.
    /// </summary>
    public int? StaleReconnectSeconds { get; set; }
}