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
}