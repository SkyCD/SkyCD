using System;
using System.IO;
using SkyCD.App;
using SkyCD.Presentation.Scanning;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class SystemMediaEjectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Eject_WithoutSource_IsNotApplicable(string? source)
    {
        var ejector = new SystemMediaEjector();

        Assert.Equal(MediaEjectionOutcome.NotApplicable, ejector.Eject(source));
        Assert.Equal(MediaEjectionSupport.SourceUnsupported, ejector.GetSupport(source));
    }

    [Fact]
    public void Eject_FolderSource_IsNotApplicable()
    {
        var folder = Path.Combine(Path.GetTempPath(), "skycd-eject-folder");
        var ejector = new SystemMediaEjector();

        Assert.Equal(MediaEjectionOutcome.NotApplicable, ejector.Eject(folder));
        Assert.Equal(MediaEjectionSupport.SourceUnsupported, ejector.GetSupport(folder));
    }

    [Fact]
    public void Eject_NetworkLocation_IsNotApplicable()
    {
        var ejector = new SystemMediaEjector();

        Assert.Equal(MediaEjectionOutcome.NotApplicable, ejector.Eject("https://example.invalid/catalog"));
    }

    [Fact]
    public void GetSupport_ReportsPlatformAndSourceSupport()
    {
        var ejector = new SystemMediaEjector();
        var expected = OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            ? MediaEjectionSupport.Supported
            : MediaEjectionSupport.PlatformUnsupported;

        Assert.Equal(expected, ejector.GetSupport(OperatingSystem.IsWindows() ? "X:" : "/dev/sr0"));
    }

    [Fact]
    public void Eject_MissingDrive_FailsWithoutThrowing()
    {
        if (!OperatingSystem.IsWindows()) return;

        // A free drive letter holds no optical drive on the test agents, so the driver refuses the request.
        var outcome = new SystemMediaEjector().Eject("Z:");

        Assert.Equal(MediaEjectionOutcome.Failed, outcome);
    }

    [Fact]
    public void IsEjectableSource_AcceptsWindowsDriveRootsOnly()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.True(SystemMediaEjector.IsEjectableSource("E:"));
        Assert.True(SystemMediaEjector.IsEjectableSource(@"E:\"));
        Assert.True(SystemMediaEjector.IsEjectableSource("e:/"));
        Assert.False(SystemMediaEjector.IsEjectableSource(@"E:\Music"));
        Assert.False(SystemMediaEjector.IsEjectableSource(@"\\server\share"));
        Assert.False(SystemMediaEjector.IsEjectableSource("folder"));
    }

    [Fact]
    public void IsEjectableSource_AcceptsUnixOpticalDeviceNodesOnly()
    {
        if (OperatingSystem.IsWindows()) return;

        Assert.True(SystemMediaEjector.IsEjectableSource("/dev/sr0"));
        Assert.True(SystemMediaEjector.IsEjectableSource("/dev/rdisk2"));
        Assert.False(SystemMediaEjector.IsEjectableSource("/home/user/disc"));
        Assert.False(SystemMediaEjector.IsEjectableSource("/dev/../etc/passwd"));
    }
}
