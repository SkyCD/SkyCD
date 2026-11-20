using System.Collections.Generic;
using SkyCD.Plugin.AudioCd;
using Xunit;

namespace SkyCD.Plugin.AudioCd.Tests;

public sealed class MacDiscTocReaderTests
{
    [Fact]
    public void ParseToc_ReadsTrackAndLeadOutAddresses()
    {
        var toc = Toc(
            (1, 0x10, 1, 0, 2, 0),
            (1, 0x10, 2, 3, 24, 63),
            (1, 0x10, 0xAA, 21, 12, 62));

        var parsed = MacDiscTocReader.ParseToc(toc, toc.Length - 2);

        Assert.NotNull(parsed);
        Assert.Equal(1, parsed!.FirstTrack);
        Assert.Equal([150, 15363], parsed.TrackOffsetsFrames);
        Assert.Equal(95462, parsed.LeadOutFrames);
    }

    [Fact]
    public void ParseToc_RejectsDataTracksAndMissingLeadOut()
    {
        var data = Toc(
            (1, 0x14, 1, 0, 2, 0),
            (1, 0x10, 0xAA, 21, 12, 62));
        var noLeadOut = Toc((1, 0x10, 1, 0, 2, 0));

        Assert.Null(MacDiscTocReader.ParseToc(data, data.Length - 2));
        Assert.Null(MacDiscTocReader.ParseToc(noLeadOut, noLeadOut.Length - 2));
    }

    [Fact]
    public void ParseToc_IgnoresDescriptorsBeyondTheReportedLength()
    {
        var toc = Toc(
            (1, 0x10, 1, 0, 2, 0),
            (1, 0x10, 2, 3, 24, 63),
            (1, 0x10, 0xAA, 21, 12, 62));

        Assert.Null(MacDiscTocReader.ParseToc(toc, 4 + 11));
    }

    [Fact]
    public void RawDevice_Conversion()
    {
        Assert.Equal("/dev/rdisk4", MacDiscTocReader.ToRawDevice("/dev/disk4"));
        Assert.Equal("/dev/rdisk4", MacDiscTocReader.ToRawDevice("/dev/rdisk4"));
    }

    [Fact]
    public void Msf_IsConvertedToAbsoluteFrames()
    {
        Assert.Equal(150, MacDiscTocReader.MsfToFrames(0, 2, 0));
        Assert.Equal(95462, MacDiscTocReader.MsfToFrames(21, 12, 62));
    }

    private static byte[] Toc(
        params (byte Session, byte Control, byte Point, byte Minute, byte Second, byte Frame)[] descriptors)
    {
        var data = new List<byte> { 0, 0, 1, 1 };
        foreach (var descriptor in descriptors)
        {
            data.Add(descriptor.Session);
            data.Add(descriptor.Control);
            data.Add(1);
            data.Add(descriptor.Point);
            data.AddRange([0, 0, 0, 0]);
            data.Add(descriptor.Minute);
            data.Add(descriptor.Second);
            data.Add(descriptor.Frame);
        }

        var length = data.Count - 2;
        data[0] = (byte)(length >> 8);
        data[1] = (byte)length;
        return data.ToArray();
    }
}
