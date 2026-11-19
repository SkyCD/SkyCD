using System;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace SkyCD.Plugin.VolumeSerial.Tests;

public sealed class WindowsVolumeSerialApiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryGetVolumeSerial_RestoresExistingThreadErrorMode(bool missingVolume)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const uint semNoOpenFileErrorBox = 0x8000;
        var originalMode = GetThreadErrorMode();
        try
        {
            Assert.True(SetThreadErrorMode(originalMode | semNoOpenFileErrorBox, out _));
            var expectedMode = GetThreadErrorMode();
            var root = missingVolume
                ? Path.Combine(Path.GetTempPath(), $"skycd-missing-volume-{Guid.NewGuid():N}")
                : Path.GetPathRoot(Path.GetTempPath())!;

            var success = new WindowsVolumeSerialApi().TryGetVolumeSerial(root, out _);

            Assert.Equal(!missingVolume, success);
            Assert.Equal(expectedMode, GetThreadErrorMode());
        }
        finally
        {
            SetThreadErrorMode(originalMode, out _);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetThreadErrorMode();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadErrorMode(uint dwNewMode, out uint lpOldMode);

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
