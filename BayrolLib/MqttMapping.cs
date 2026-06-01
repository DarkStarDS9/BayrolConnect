using System.Text.Json;

namespace BayrolLib;

/// <summary>
/// These mappings have been determined by looking at code from
/// https://knx-user-forum.de/forum/%C3%B6ffentlicher-bereich/knx-eib-forum/1890228-lbs-bayrol-automatic-salt-14338
/// and by reverse engineering the MQTT messages sent by the Bayrol Web-App.
/// </summary>
public static class MqttMapping
{
    public const string DeviceStatus = "1";
    public const string PhValue = "4.78";
    public const string RedoxValue = "4.82";
    public const string RedoxTargetValue = "4.28";
    public const string TemperatureValue = "4.98";
    public const string SaltValue = "4.100";
    public const string FilterPumpState = "5.29";
    public const string PhAutomationState = "5.42";
    public const string SaltProductionState = "5.40";
    public const string BoostModeState = "5.104";
    public const string PhDosingRate = "4.89";
    public const string SaltProductionRate = "4.91";
    public const string CanisterState = "5.80";

    // SE manual mode control (SET — also echo-subscribed to confirm before start)
    public const string SeManualPowerPct = "4.149";
    public const string SeManualRuntime = "4.77";
    public const string SeManualOrpShutoff = "4.150";

    // SE manual mode function calls (13.x namespace, v=1 to trigger — write-only, no subscribe)
    public const string SeManualStart = "13.67";
    // SE manual mode stop (5.x enum, "19.18" = deactivate — write-only, no subscribe)
    public const string SeManualStop = "5.136";

    // SE manual mode status (subscribe)
    public const string SeManualActive = "5.131";
    public const string SeManualProgressMin = "4.156";

    // Weighted operating hours — Zeit × % in minutes (subscribe)
    public const string WeightedOpTimeMin = "4.188";

    // Topics subscribed for SE manual echo-wait only — device does NOT publish these
    // proactively, so they must NOT go into _uninitializedTopics.
    public static readonly string[] ControlEchoTopics =
    [
        SeManualPowerPct,
        SeManualRuntime,
        SeManualOrpShutoff,
    ];

    // Topics the device publishes proactively — used for subscription + init tracking.
    // Control/function topics (4.149, 4.77, 4.150, 13.67) are intentionally excluded.
    public static readonly string[] AllTopics;

    static MqttMapping()
    {
        var controlTopics = new HashSet<string>(ControlEchoTopics) { SeManualStart, SeManualStop };
        AllTopics = typeof(MqttMapping).GetFields()
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .Where(t => !controlTopics.Contains(t))
            .ToArray();
    }

    public static bool ToBool(JsonElement element)
    {
        var str = element.GetString();

        return str
            is "19.17"      // ???
            or "19.96"      // ???
            or "19.258";    // canister OK
    }
    
    public static DeviceState ToDeviceState(JsonElement element)
    {
        var str = element.GetString();

        return str switch
        {
            "17.4" => DeviceState.Ok,
            "17.3" => DeviceState.Warning,
            "17.0" => DeviceState.Offline,
            _ => DeviceState.Error
        };
    }
}