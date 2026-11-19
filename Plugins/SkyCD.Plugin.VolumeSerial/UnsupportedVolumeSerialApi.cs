namespace SkyCD.Plugin.VolumeSerial;

/// <summary>Platform stub for systems that expose no volume serial API.</summary>
internal sealed class UnsupportedVolumeSerialApi : IVolumeSerialApi
{
    public static UnsupportedVolumeSerialApi Instance { get; } = new();

    private UnsupportedVolumeSerialApi()
    {
    }

    public bool IsSupported => false;

    public bool TryGetVolumeSerial(string rootPath, out uint volumeSerial)
    {
        volumeSerial = 0;
        return false;
    }
}
