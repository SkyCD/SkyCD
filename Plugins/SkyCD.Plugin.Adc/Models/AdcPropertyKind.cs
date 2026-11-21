namespace SkyCD.Plugin.Adc.Models;

/// <summary>
/// Payload encoding selected by the high two bits of a property descriptor.
/// </summary>
public enum AdcPropertyKind
{
    Uint32 = 0,
    String = 1,
    Blob = 2
}
