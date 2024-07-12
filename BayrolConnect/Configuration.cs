using Microsoft.Extensions.Logging;

namespace BayrolConnect;

public class Configuration(string user, string password, string cid, string idScope, string deviceId, string primaryKey, LogLevel logLevel = LogLevel.Information)
{
    public string User { get; } = user;
    public string Password { get; } = password;
    public string Cid { get; } = cid;
    public string IdScope { get; } = idScope;
    public string DeviceId { get; } = deviceId;
    public string PrimaryKey { get; } = primaryKey;
    public LogLevel LogLevel { get; } = logLevel;
    
    public bool UseMqtt { get; set; }

    public Dictionary<TimeSpan, int>? RedoxTargetValues { get; set; }
}