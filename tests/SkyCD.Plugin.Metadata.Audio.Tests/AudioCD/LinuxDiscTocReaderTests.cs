using System.Collections.Generic;
using System.Threading;
using SkyCD.Plugin.Metadata.Audio.AudioCD;
using Xunit;
using SkyCD.Plugin.Metadata.Audio.AudioCD.Parsing;

namespace SkyCD.Plugin.Metadata.Audio.Tests.AudioCD;

public sealed class LinuxDiscTocReaderTests
{
    [Fact]
    public void MountTable_ResolvesTheLongestMountPoint()
    {
        var mountTable = string.Join('\n',
            "/dev/sda1 / ext4 rw 0 0",
            "/dev/sr0 /media/user/My\\040Disc iso9660 ro 0 0",
            "/dev/sr1 /home/user ext4 rw 0 0",
            "tmpfs /run tmpfs rw 0 0");

        Assert.Equal("/dev/sr0", LinuxDiscTocReader.FindMountedDevice(mountTable, "/media/user/My Disc/Track01.cda"));
        Assert.Equal("/dev/sr0", LinuxDiscTocReader.FindMountedDevice(mountTable, "/media/user/My Disc"));
        Assert.Equal("/dev/sr1", LinuxDiscTocReader.FindMountedDevice(mountTable, "/home/user/disc"));
        Assert.Null(LinuxDiscTocReader.FindMountedDevice(mountTable, "/mnt/other"));
        Assert.Null(LinuxDiscTocReader.FindMountedDevice("/dev/sr0 /media/user/CD iso9660 ro 0 0",
            "/media/user/CDExtra"));
    }

    [Fact]
    public void DeviceNode_IsUsedDirectly_WhenItExists()
    {
        Assert.Null(LinuxDiscTocReader.ResolveDevice("/dev/skycd-missing-sr", null));
        Assert.Null(LinuxDiscTocReader.ResolveDevice("/mnt/cd", null));
    }

    [Fact]
    public void AssembleToc_BuildsTheFramesTheDiscIdNeeds()
    {
        var tracks = new List<DiscTocEntry>
        {
            new(150, false),
            new(15363, false),
            new(32314, false)
        };

        var toc = LinuxDiscTocReader.AssembleToc(1, tracks, new DiscTocEntry(95462, false));

        Assert.NotNull(toc);
        Assert.Equal(1, toc!.FirstTrack);
        Assert.Equal(3, toc.TrackCount);
        Assert.Equal([150, 15363, 32314], toc.TrackOffsetsFrames);
        Assert.Equal(95462, toc.LeadOutFrames);
    }

    [Fact]
    public void AssembleToc_RejectsDataTracksAndBadOrdering()
    {
        var audio = new List<DiscTocEntry> { new(150, false), new(15363, false) };

        Assert.Null(LinuxDiscTocReader.AssembleToc(1, [new DiscTocEntry(150, true), new DiscTocEntry(200, false)],
            new DiscTocEntry(1000, false)));
        Assert.Null(LinuxDiscTocReader.AssembleToc(1, audio, new DiscTocEntry(900, true)));
        Assert.Null(LinuxDiscTocReader.AssembleToc(1, [new DiscTocEntry(300, false), new DiscTocEntry(200, false)],
            new DiscTocEntry(1000, false)));
        Assert.Null(LinuxDiscTocReader.AssembleToc(1, [], new DiscTocEntry(1000, false)));
    }

    [Fact]
    public void Msf_IsConvertedToAbsoluteFrames()
    {
        Assert.Equal(150, LinuxDiscTocReader.MsfToFrames(0, 2, 0));
        Assert.Equal(95462, LinuxDiscTocReader.MsfToFrames(21, 12, 62));
    }

    [Fact]
    public void Read_ReturnsNullForUnusableRoots()
    {
        var reader = new PlatformDiscTocReader();

        Assert.Null(reader.Read("/dev/skycd-missing-sr", CancellationToken.None));
        Assert.Null(reader.Read(string.Empty, CancellationToken.None));
    }
}
