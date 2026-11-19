using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace SkyCD.Plugin.VolumeSerial;

/// <summary>
/// Filesystem volume serial via <c>GetVolumeInformationW</c>: ordinary read-only OS access that needs
/// no elevation and never reads file contents. A non-genuine or inaccessible volume reports failure
/// through the return value instead of fabricating a serial.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsVolumeSerialApi : IVolumeSerialApi
{
    public bool IsSupported => true;

    public bool TryGetVolumeSerial(string rootPath, out uint volumeSerial)
    {
        volumeSerial = 0;
        return !string.IsNullOrWhiteSpace(rootPath)
            && GetVolumeInformation(rootPath, null, 0, out volumeSerial, out _, out _, null, 0);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(
        string lpRootPathName,
        StringBuilder? lpVolumeNameBuffer,
        int nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder? lpFileSystemNameBuffer,
        int nFileSystemNameSize);
}
