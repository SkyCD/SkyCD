namespace SkyCD.Plugin.Metadata.Volume.Sources;

/// <summary>Read-only OS volume identity seam; exists so tests can fake every platform outcome.</summary>
internal interface IVolumeSerialApi
{
    /// <summary>False when the current platform cannot query volume serials.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// True and fills <paramref name="volumeSerial"/> on a genuine report — including a genuinely
    /// reported zero; false on any API failure or when the volume exposes no serial.
    /// </summary>
    bool TryGetVolumeSerial(string rootPath, out uint volumeSerial);
}
