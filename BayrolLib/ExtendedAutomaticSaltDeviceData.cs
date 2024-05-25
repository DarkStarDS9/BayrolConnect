namespace BayrolLib;

/// <summary>
/// When you want / need more data, use the <see cref="BayrolMqttConnector"/> that will return this class.
/// MQTT is a much more complex implementation with many assumptions because there is no public api available.
/// </summary>
public class ExtendedAutomaticSaltDeviceData : AutomaticSaltDeviceData
{
    public bool FilterPumpState { get; set; }
    public bool PhAutomationState { get; set; }
    public bool SaltProductionState { get; set; }
    public bool BoostModeState { get; set; }
    public decimal PhDosingRate { get; set; }
    public decimal SaltProductionRate { get; set; }
    public bool CanisterState { get; set; }

    public ExtendedAutomaticSaltDeviceData Clone()
        => (ExtendedAutomaticSaltDeviceData)MemberwiseClone();
}