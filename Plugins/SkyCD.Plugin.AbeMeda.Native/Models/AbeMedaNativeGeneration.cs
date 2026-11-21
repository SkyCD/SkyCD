namespace SkyCD.Plugin.AbeMeda.Native.Models;

/// <summary>
/// Catalog generations recognized by the family. Only the classic container has
/// verified byte-level fixtures; the extended generation is gated until it does.
/// </summary>
public enum AbeMedaNativeGeneration
{
    Classic,
    Extended
}
