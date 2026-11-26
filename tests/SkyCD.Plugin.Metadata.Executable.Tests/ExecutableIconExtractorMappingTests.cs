using System;
using SkyCD.Plugin.Metadata.Executable.Parsing;
using Xunit;

namespace SkyCD.Plugin.Metadata.Executable.Tests;

/// <summary>
/// The icon-directory mapping (issue #1221): group records parsed through MetadataExtractor
/// yield the same candidates as the hand reader did, including 256-pixel entries and the skip
/// of zero-length images.
/// </summary>
public class ExecutableIconExtractorMappingTests
{
    [Fact]
    public void ParseGroup_MapsEntriesThroughMetadataExtractor()
    {
        var directory = BuildGroup((16, 16, 32, 40), (32, 32, 24, 40));

        var candidates = ExecutableIconExtractor.ParseGroup(directory);

        Assert.NotNull(candidates);
        Assert.Equal(2, candidates.Count);
        Assert.Equal((1, 16, 16, 32), (candidates[0].ImageIndex, candidates[0].Width, candidates[0].Height, candidates[0].BitCount));
        Assert.Equal((2, 32, 32, 24), (candidates[1].ImageIndex, candidates[1].Width, candidates[1].Height, candidates[1].BitCount));
    }

    [Fact]
    public void ParseGroup_MapsZeroSizeToLargestDeclaredSize()
    {
        var directory = BuildGroup((0, 0, 32, 40));

        var candidates = ExecutableIconExtractor.ParseGroup(directory);

        var candidate = Assert.Single(candidates!);
        Assert.Equal(256, candidate.Width);
        Assert.Equal(256, candidate.Height);
    }

    [Fact]
    public void ParseGroup_SkipsZeroLengthImages()
    {
        var directory = BuildGroup((16, 16, 32, 0), (32, 32, 24, 40));

        var candidates = ExecutableIconExtractor.ParseGroup(directory);

        var candidate = Assert.Single(candidates!);
        Assert.Equal(2, candidate.ImageIndex);
        Assert.Equal(32, candidate.Width);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void ParseGroup_RejectsTruncatedDirectories(int length)
    {
        var directory = new byte[length];

        Assert.Null(ExecutableIconExtractor.ParseGroup(directory));
    }

    [Fact]
    public void ParseGroup_RejectsExcessiveCounts()
    {
        var directory = new byte[6];
        BitConverter.GetBytes((ushort)1).CopyTo(directory, 2);
        BitConverter.GetBytes((ushort)300).CopyTo(directory, 4);

        Assert.Null(ExecutableIconExtractor.ParseGroup(directory));
    }

    private static byte[] BuildGroup(params (int Width, int Height, ushort Bits, uint Size)[] entries)
    {
        var directory = new byte[6 + (entries.Length * 14)];
        BitConverter.GetBytes((ushort)0).CopyTo(directory, 0);
        BitConverter.GetBytes((ushort)1).CopyTo(directory, 2);
        BitConverter.GetBytes((ushort)entries.Length).CopyTo(directory, 4);
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = directory.AsSpan(6 + (i * 14), 14);
            entry[0] = (byte)entries[i].Width;
            entry[1] = (byte)entries[i].Height;
            BitConverter.GetBytes((ushort)1).CopyTo(entry.Slice(4, 2));
            BitConverter.GetBytes(entries[i].Bits).CopyTo(entry.Slice(6, 2));
            BitConverter.GetBytes(entries[i].Size).CopyTo(entry.Slice(8, 4));
            BitConverter.GetBytes((ushort)(i + 1)).CopyTo(entry.Slice(12, 2));
        }

        return directory;
    }
}
