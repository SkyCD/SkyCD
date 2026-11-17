namespace SkyCD.Plugin.BrokenX;

/// <summary>
/// Payload encoding selected by descriptor bits 7..6.
/// </summary>
public enum BxdPropertyKind
{
    Uint32 = 0,
    Text = 1,
    Blob = 2
}
