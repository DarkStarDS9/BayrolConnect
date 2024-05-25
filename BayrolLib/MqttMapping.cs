using System.Text.Json;

namespace BayrolLib;

/// <summary>
/// These mappings have been determined by looking at code from
/// https://knx-user-forum.de/forum/%C3%B6ffentlicher-bereich/knx-eib-forum/1890228-lbs-bayrol-automatic-salt-14338
/// and by reverse engineering the MQTT messages sent by the Bayrol Web-App.
/// </summary>
public static class MqttMapping
{
    public const string PhValue = "4.2";
    public const string RedoxValue = "4.82";
    public const string TemperatureValue = "4.98";
    public const string SaltValue = "4.100";
    public const string FilterPumpState = "5.29";
    public const string PhAutomationState = "5.42";
    public const string SaltProductionState = "5.40";
    public const string BoostModeState = "5.104";
    public const string PhDosingRate = "4.89";
    public const string SaltProductionRate = "4.91";
    public const string CanisterState = "5.80";

    public static readonly string[] AllTopics;

    static MqttMapping()
    {
        // set AllTopics using reflection
        AllTopics = typeof(MqttMapping).GetFields()
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null))
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
    
    /*     
      case 2: {
         // Device status offline
         this.I_Receive( 'd02/22ASE2-12345/v/1', '{"v":"17.0"}' );
         break;
       }
       case 3: {
         // Device status online
         this.I_Receive( 'd02/22ASE2-12345/v/1', '{"v":"17.4"}' );
         break;
       }
     */
}