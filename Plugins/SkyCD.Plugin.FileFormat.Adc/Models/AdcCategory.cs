namespace SkyCD.Plugin.FileFormat.Adc.Models;

/// <summary>
/// A user-assigned category that items may reference by identifier.
/// </summary>
public sealed class AdcCategory
{
    public uint CategoryId { get; set; }

    public string Name { get; set; } = string.Empty;
}
