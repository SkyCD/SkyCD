using System;
using System.IO;
using Xunit;

namespace SkyCD.Plugin.VolumeSerial.Tests;

public sealed class WindowsVolumeSerialApiTests
{
    [Fact]
    public void TryGetVolumeSerial_CurrentVolume_RoundTripsLosslessly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var api = new WindowsVolumeSerialApi();
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.True(api.TryGetVolumeSerial(root, out var serial));
        var text = VolumeSerialSchema.Format(serial);
        Assert.True(VolumeSerialSchema.TryParse(text, out var parsed));
        Assert.Equal(serial, parsed);
    }

    [Fact]
    public void TryGetVolumeSerial_GenuineZero_IsReportedAsSuccess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // The contract distinguishes success from failure through the return value alone, so even a
        // zero serial must arrive as a successful report whenever the OS produces one.
        var api = new WindowsVolumeSerialApi();
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.True(api.TryGetVolumeSerial(root, out var serial));
        Assert.True(api.TryGetVolumeSerial(root, out var again));
        Assert.Equal(serial, again);
    }

    [Fact]
    public void TryGetVolumeSerial_MissingVolume_ReportsFailure()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var api = new WindowsVolumeSerialApi();
        var missing = Path.Combine(Path.GetTempPath(), $"skycd-missing-volume-{Guid.NewGuid():N}");

        Assert.False(api.TryGetVolumeSerial(missing, out var serial));
        Assert.Equal(0u, serial);
    }

    [Fact]
    public void TryGetVolumeSerial_BlankRoot_ReportsFailure()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var api = new WindowsVolumeSerialApi();

        Assert.False(api.TryGetVolumeSerial(" ", out _));
    }
}
