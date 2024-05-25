namespace BayrolLib;

/// <summary>
/// This represents the data obtainable from AS5 / AS7 devices.
/// </summary>
public class AutomaticSaltDeviceData
{
    public decimal Ph { get; set; }
    
    /// <summary>
    /// Redox potential in mV
    /// </summary>
    public int Redox { get; set; }
    
    /// <summary>
    ///  Temperature in °C
    /// </summary>
    public decimal Temperature { get; set; }
    
    /// <summary>
    /// Salt in g/l
    /// </summary>
    public decimal Salt { get; set; }
    
    /// <summary>
    /// Date + Time when the data was obtained
    /// </summary>
    public DateTimeOffset ObtainedAt { get; set; }
    
    /// <summary>
    /// If the device is OK and the values are valid
    /// </summary>
    public DeviceState DeviceState { get; set; }
    
    /// <summary>
    /// If the device is not OK, this should contain the error message
    /// </summary>
    public string? ErrorMessage { get; set; }
}