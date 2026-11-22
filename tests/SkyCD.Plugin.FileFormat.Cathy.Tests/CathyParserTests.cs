using System;
using System.IO;
using SkyCD.Plugin.FileFormat.Cathy.Exceptions;
using Xunit;
using SkyCD.Plugin.FileFormat.Cathy.Import;
using SkyCD.Plugin.FileFormat.Cathy.Parsing;

namespace SkyCD.Plugin.FileFormat.Cathy.Tests;

public class CathyParserTests
{
    [Fact]
    public void CanHandleStream_ValidMagic_ReturnsTrue()
    {
        var bytes = CathyTestHelper.CreateSampleCaf(version: 8);
        using var ms = new MemoryStream(bytes);

        Assert.True(CathyParser.IsCathyStream(ms));
        Assert.Equal(0, ms.Position);
    }

    [Fact]
    public void CanHandleStream_InvalidMagic_ReturnsFalse()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        using var ms = new MemoryStream(bytes);

        Assert.False(CathyParser.IsCathyStream(ms));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Parse_SupportedVersions_SuccessfullyParses(short version)
    {
        var bytes = CathyTestHelper.CreateSampleCaf(
            version: version,
            volumeName: $"Volume_v{version}",
            comment: $"Comment_v{version}");

        using var ms = new MemoryStream(bytes);
        var catalog = CathyParser.Parse(ms);

        Assert.Equal(version, catalog.Header.Version);
        Assert.Equal($"Volume_v{version}", catalog.Header.Volume);
        Assert.Equal(2, catalog.Folders.Count);
        Assert.Equal(3, catalog.Entries.Count);
    }

    [Fact]
    public void Parse_CorruptHeader_ThrowsException()
    {
        var bytes = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        using var ms = new MemoryStream(bytes);

        Assert.Throws<CathyCorruptHeaderException>(() => CathyParser.Parse(ms));
    }

    [Fact]
    public void Parse_IncompatibleVersion_ThrowsException()
    {
        var bytes = CathyTestHelper.CreateSampleCaf(version: 15);
        using var ms = new MemoryStream(bytes);

        Assert.Throws<CathyIncompatibleVersionException>(() => CathyParser.Parse(ms));
    }

    [Fact]
    public void HierarchyBuilder_BuildsCorrectTreeAndSummary()
    {
        var bytes = CathyTestHelper.CreateSampleCaf(version: 8, volumeName: "Archive1", comment: "My Backup");
        using var ms = new MemoryStream(bytes);
        var catalog = CathyParser.Parse(ms);

        var (entries, summary) = CathyHierarchyBuilder.BuildHierarchy(catalog);

        Assert.Equal("Archive1", summary.VolumeName);
        Assert.Equal(2, summary.TotalFolders);
        Assert.Equal(2, summary.TotalFiles);
        Assert.Equal(1536, summary.TotalSizeBytes);

        // Reconstructed nodes
        Assert.Contains(entries, e => e.FullPath == "Archive1" && e.IsDirectory);
        Assert.Contains(entries, e => e.FullPath == "Archive1/Docs" && e.IsDirectory);
        Assert.Contains(entries, e => e.FullPath == "Archive1/Readme.txt" && !e.IsDirectory && e.Size == 512);
        Assert.Contains(entries, e => e.FullPath == "Archive1/Docs/Manual.pdf" && !e.IsDirectory && e.Size == 1024);
    }
}
