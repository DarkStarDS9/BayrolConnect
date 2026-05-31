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

    /// <summary>
    /// Weighted operating hours counter: cumulative Zeit×% in minutes (topic 4.188).
    /// Divide by 60 for hours.
    /// </summary>
    public int WeightedOpTimeMinutes { get; set; }

    /// <summary>
    /// Whether a timed SE manual production run is currently active (topic 5.131, v=17).
    /// </summary>
    public bool SeManualActive { get; set; }

    /// <summary>
    /// Remaining runtime of the active manual production run in minutes (topic 4.156).
    /// </summary>
    public int SeManualProgressMinutes { get; set; }

    public ExtendedAutomaticSaltDeviceData Clone()
        => (ExtendedAutomaticSaltDeviceData)MemberwiseClone();
}