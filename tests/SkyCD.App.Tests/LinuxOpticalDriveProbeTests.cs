using System;
using System.IO;
using SkyCD.Presentation.Scanning;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class LinuxOpticalDriveProbeTests : IDisposable
{
    private readonly TestWorkspace workspace = new();
    private readonly string sysfs;
    private readonly string devices;

    private string root => workspace.Root;

    public LinuxOpticalDriveProbeTests()
    {
        sysfs = Path.Combine(root, "sys");
        devices = Path.Combine(root, "dev");
        foreach (var name in new[] { "sr0", "sr1", "sr2", "sda", "sr" })
        {
            Directory.CreateDirectory(Path.Combine(sysfs, name));
        }

        Directory.CreateDirectory(devices);
        foreach (var name in new[] { "sr0", "sr1", "sda" })
        {
            File.WriteAllText(Path.Combine(devices, name), string.Empty);
        }
    }

    public void Dispose()
    {
        workspace.Dispose();
    }

    [Fact]
    public void ListsOnlyOpticalDrivesThatHaveMediaAndADeviceNode()
    {
        var drives = LinuxOpticalDriveProbe.GetDrives(sysfs, devices, null,
            device => device.EndsWith("sr0", StringComparison.Ordinal));

        var drive = Assert.Single(drives);
        Assert.Equal(Path.Combine(devices, "sr0"), drive.RootDirectory);
        Assert.Equal(DriveType.CDRom, drive.DriveType);
        Assert.True(drive.IsReady);
    }

    [Fact]
    public void SkipsMountedDevices_BecauseTheMountIsTheScannableRoot()
    {
        var mountTable = $"{Path.Combine(devices, "sr0")} /media/user/AUDIO_CD iso9660 ro 0 0";

        var drives = LinuxOpticalDriveProbe.GetDrives(sysfs, devices, mountTable, _ => true);

        var drive = Assert.Single(drives);
        Assert.Equal(Path.Combine(devices, "sr1"), drive.RootDirectory);
    }

    [Fact]
    public void MountTable_MatchesWholeMountPoints()
    {
        var mountTable = "/dev/sr0 /media/user/CD iso9660 ro 0 0";

        Assert.True(LinuxOpticalDriveProbe.IsMounted("/dev/sr0", mountTable));
        Assert.False(LinuxOpticalDriveProbe.IsMounted("/dev/sr1", mountTable));
        Assert.False(LinuxOpticalDriveProbe.IsMounted("/dev/sr0", null));
    }
}
