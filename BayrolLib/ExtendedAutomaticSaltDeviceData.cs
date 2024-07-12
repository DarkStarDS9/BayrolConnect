namespace BayrolLib;

/// <summary>
/// When you want / need more data, use the <see cref="BayrolMqttConnector"/> that will return this class.
/// MQTT is a much more complex implementation with many assumptions because there is no public api available.
/// </summary>
public class ExtendedAutomaticSaltDeviceData : AutomaticSaltDeviceData
{
    /// <summary>
    /// Target Redox potential in mV
    /// </summary>
    public int RedoxTarget { get; set; }
    
    /// <summary>
    /// Is the pump running?
    /// </summary>
    public bool FilterPumpState { get; set; }
    
    /// <summary>
    /// PH dosing enabled?
    /// </summary>
    public bool PhAutomationState { get; set; }
    
    /// <summary>
    /// Is the Salt Water Chlorine Generators (SWG) enabled?
    /// </summary>
    public bool SaltProductionState { get; set; }
    
    /// <summary>
    /// Is boost mode enabled on the SWG?
    /// </summary>
    public bool BoostModeState { get; set; }
    
    /// <summary>
    /// Rate at which we are currently dosing PH-
    /// </summary>
    public int PhDosingRate { get; set; }
    
    /// <summary>
    /// Rate at which the SWG is currently running (percentage)
    /// </summary>
    public int SaltProductionRate { get; set; }
    
    /// <summary>
    /// Is the PH- canister still full?
    /// </summary>
    public bool CanisterState { get; set; }

    public ExtendedAutomaticSaltDeviceData Clone()
        => (ExtendedAutomaticSaltDeviceData)MemberwiseClone();
}