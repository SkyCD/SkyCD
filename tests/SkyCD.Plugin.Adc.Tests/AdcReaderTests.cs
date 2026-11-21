using System;
using System.IO;
using System.Linq;
using System.Threading;
using SkyCD.Plugin.Adc.Exceptions;
using Xunit;
using SkyCD.Plugin.Adc.Models;
using SkyCD.Plugin.Adc.Parsing;

namespace SkyCD.Plugin.Adc.Tests;

public sealed class AdcReaderTests
{
    [Fact]
    public void Read_EmptyCatalog_ReturnsEmptyResult()
    {
        var catalog = AdcReader.Read(StreamOf(AdcFixtureWriter.Write(AdcFixtures.EmptyCatalog())));

        Assert.Empty(catalog.Volumes);
        Assert.Empty(catalog.Items);
        Assert.Empty(catalog.Categories);
        Assert.Empty(catalog.Comments);
        Assert.Equal(0, catalog.TotalRecordCount);
    }

    [Fact]
    public void Read_NestedCatalog_PreservesHierarchyEmptyFoldersAndMetadata()
    {
        var catalog = AdcReader.Read(StreamOf(AdcFixtureWriter.Write(AdcFixtures.NestedCatalog())));

        var volume = Assert.Single(catalog.Volumes);
        Assert.Equal(AdcVolumeType.Fixed, volume.VolumeType);
        Assert.Equal(0x1A2B3C4DU, volume.SerialNumber);
        Assert.Equal(5, catalog.Items.Count);

        var empty = Assert.Single(catalog.Items, item => item.Name == "Empty");
        Assert.Equal(AdcItemKind.Folder, empty.Kind);

        var main = Assert.Single(catalog.Items, item => item.Name == "main.cs");
        Assert.Equal(1234UL, main.SizeBytes);
        Assert.Equal((byte)0x20, main.Attributes);
        Assert.Equal(7U, main.TypeCode);
        Assert.Equal(3, main.Properties.Count);
        Assert.Equal("custom-note", main.Properties[1].Text);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, main.Properties[2].Blob);
    }

    [Fact]
    public void Read_MultiVolumeCatalog_PreservesUnicodeAndLargeSizes()
    {
        var catalog = AdcReader.Read(StreamOf(AdcFixtureWriter.Write(AdcFixtures.MultiVolumeCatalog())));

        Assert.Equal(2, catalog.Volumes.Count);
        Assert.Equal("АРХИВ", catalog.Volumes[0].Label);
        Assert.Equal("日本語", catalog.Volumes[1].Label);

        var huge = Assert.Single(catalog.Items, item => item.Name == "huge.iso");
        Assert.Equal(5_368_709_120UL, huge.SizeBytes);
        Assert.True(huge.SizeBytes > uint.MaxValue);
    }

    [Fact]
    public void Read_CommentsAndCategories_AreResolved()
    {
        var catalog = AdcReader.Read(StreamOf(AdcFixtureWriter.Write(AdcFixtures.CommentsCatalog())));

        Assert.Equal(3, catalog.Comments.Count);
        Assert.Equal(2, catalog.Categories.Count);
        var setupA = Assert.Single(catalog.Items, item => item.ItemId == 31);
        Assert.Equal("Installer setup", catalog.Comments[(int)setupA.CommentIndex - 1]);
        Assert.Equal([1U, 2U], setupA.CategoryIds);
    }

    [Fact]
    public void Read_DuplicateBasenames_KeepDistinctParents()
    {
        var catalog = AdcReader.Read(StreamOf(AdcFixtureWriter.Write(AdcFixtures.CommentsCatalog())));

        var duplicates = catalog.Items.Where(item => item.Name == "setup.exe").ToList();
        Assert.Equal(2, duplicates.Count);
        Assert.Equal(2, duplicates.Select(item => item.ParentId).Distinct().Count());
    }

    [Fact]
    public void Read_UnknownRecordType_IsSkippedAndReported()
    {
        var bytes = AdcFixtureWriter.Write(
            AdcFixtures.NestedCatalog(),
            [new AdcRawRecord(0x0000007F, [9, 9, 9])]);

        var catalog = AdcReader.Read(StreamOf(bytes));

        Assert.Equal(5, catalog.Items.Count);
        Assert.Contains("record:0x0000007F", catalog.UnsupportedFields);
    }

    [Fact]
    public void Read_DuplicateItemId_Throws()
    {
        var catalog = AdcFixtures.NestedCatalog();
        catalog.Items.Add(new AdcItem
        {
            ItemId = 10,
            ParentId = 0,
            VolumeId = 1,
            Kind = AdcItemKind.Folder,
            Name = "duplicate"
        });

        Assert.Throws<AdcDuplicateIdException>(() =>
            AdcReader.Read(StreamOf(AdcFixtureWriter.Write(catalog))));
    }

    [Fact]
    public void Read_ParentCycle_Throws()
    {
        var catalog = AdcFixtures.EmptyCatalog();
        catalog.Volumes.Add(new AdcVolume { VolumeId = 1, Label = "VOL" });
        catalog.Items.Add(new AdcItem
        {
            ItemId = 1,
            ParentId = 2,
            VolumeId = 1,
            Kind = AdcItemKind.Folder,
            Name = "a"
        });
        catalog.Items.Add(new AdcItem
        {
            ItemId = 2,
            ParentId = 1,
            VolumeId = 1,
            Kind = AdcItemKind.Folder,
            Name = "b"
        });

        Assert.Throws<AdcCycleException>(() => AdcReader.Read(StreamOf(AdcFixtureWriter.Write(catalog))));
    }

    [Fact]
    public void Read_UnknownParent_Throws()
    {
        var catalog = AdcFixtures.EmptyCatalog();
        catalog.Volumes.Add(new AdcVolume { VolumeId = 1, Label = "VOL" });
        catalog.Items.Add(new AdcItem
        {
            ItemId = 1,
            ParentId = 99,
            VolumeId = 1,
            Kind = AdcItemKind.File,
            Name = "orphan.txt"
        });

        Assert.Throws<AdcCorruptException>(() => AdcReader.Read(StreamOf(AdcFixtureWriter.Write(catalog))));
    }

    [Fact]
    public void Read_FileAsParent_Throws()
    {
        var catalog = AdcFixtures.EmptyCatalog();
        catalog.Volumes.Add(new AdcVolume { VolumeId = 1, Label = "VOL" });
        catalog.Items.Add(new AdcItem
        {
            ItemId = 1,
            ParentId = 0,
            VolumeId = 1,
            Kind = AdcItemKind.File,
            Name = "plain.txt"
        });
        catalog.Items.Add(new AdcItem
        {
            ItemId = 2,
            ParentId = 1,
            VolumeId = 1,
            Kind = AdcItemKind.File,
            Name = "child.txt"
        });

        Assert.Throws<AdcCorruptException>(() => AdcReader.Read(StreamOf(AdcFixtureWriter.Write(catalog))));
    }

    [Fact]
    public void Read_UnknownVolume_Throws()
    {
        var catalog = AdcFixtures.EmptyCatalog();
        catalog.Items.Add(new AdcItem
        {
            ItemId = 1,
            ParentId = 0,
            VolumeId = 9,
            Kind = AdcItemKind.File,
            Name = "orphan.txt"
        });

        Assert.Throws<AdcCorruptException>(() => AdcReader.Read(StreamOf(AdcFixtureWriter.Write(catalog))));
    }

    [Fact]
    public void Read_ExcessiveNesting_Throws()
    {
        var catalog = AdcFixtures.EmptyCatalog();
        catalog.Volumes.Add(new AdcVolume { VolumeId = 1, Label = "VOL" });
        for (uint id = 1; id <= AdcFormat.MaxItemDepth + 2; id++)
        {
            catalog.Items.Add(new AdcItem
            {
                ItemId = id,
                ParentId = id - 1,
                VolumeId = 1,
                Kind = AdcItemKind.Folder,
                Name = $"d{id}"
            });
        }

        Assert.Throws<AdcLimitExceededException>(() =>
            AdcReader.Read(StreamOf(AdcFixtureWriter.Write(catalog))));
    }

    [Fact]
    public void Read_InvalidUtf16_Throws()
    {
        var catalog = AdcFixtures.EmptyCatalog();
        catalog.Volumes.Add(new AdcVolume { VolumeId = 1, Label = "VOL" });
        catalog.Items.Add(new AdcItem
        {
            ItemId = 1,
            ParentId = 0,
            VolumeId = 1,
            Kind = AdcItemKind.File,
            Name = "AB"
        });

        var bytes = AdcFixtureWriter.Write(catalog);
        var nameOffset = IndexOf(bytes, [0x41, 0x00, 0x42, 0x00]);
        Assert.True(nameOffset >= 0);
        bytes[nameOffset + 2] = 0x00;
        bytes[nameOffset + 3] = 0xD8;

        Assert.Throws<AdcCorruptException>(() => AdcReader.Read(StreamOf(bytes)));
    }

    [Fact]
    public void Read_InvalidTimestamp_Throws()
    {
        var catalog = AdcFixtures.EmptyCatalog();
        catalog.Volumes.Add(new AdcVolume { VolumeId = 1, Label = "VOL" });
        catalog.Items.Add(new AdcItem
        {
            ItemId = 1,
            ParentId = 0,
            VolumeId = 1,
            Kind = AdcItemKind.File,
            Name = "bad.txt",
            CreatedFileTime = ulong.MaxValue
        });

        Assert.Throws<AdcCorruptException>(() => AdcReader.Read(StreamOf(AdcFixtureWriter.Write(catalog))));
    }

    [Fact]
    public void Read_MalformedBodyOffset_Throws()
    {
        var bytes = AdcFixtureWriter.Write(AdcFixtures.NestedCatalog());
        WriteUInt64(bytes, AdcFormat.BodyOffsetOffset, 1);

        Assert.Throws<AdcMalformedOffsetException>(() => AdcReader.Read(StreamOf(bytes)));
    }

    [Fact]
    public void Read_MalformedCommentTableOffset_Throws()
    {
        var bytes = AdcFixtureWriter.Write(AdcFixtures.NestedCatalog());
        WriteUInt64(bytes, AdcFormat.CommentTableOffsetOffset, 0);

        Assert.Throws<AdcMalformedOffsetException>(() => AdcReader.Read(StreamOf(bytes)));
    }

    [Fact]
    public void Read_LyingRecordCount_ThrowsTruncatedRecord()
    {
        var bytes = AdcFixtureWriter.Write(AdcFixtures.NestedCatalog());
        var recordCount = ReadUInt32(bytes, AdcFormat.RecordCountOffset);
        WriteUInt32(bytes, AdcFormat.RecordCountOffset, recordCount + 1);

        Assert.Throws<AdcTruncatedRecordException>(() => AdcReader.Read(StreamOf(bytes)));
    }

    [Fact]
    public void Read_TruncatedFile_ThrowsTruncatedRecord()
    {
        Assert.Throws<AdcTruncatedRecordException>(() =>
            AdcReader.Read(StreamOf(AdcFixtures.TruncatedNested())));
    }

    [Fact]
    public void Read_AuthenticodeCatalog_Throws()
    {
        Assert.Throws<AdcAuthenticodeCatalogException>(() =>
            AdcReader.Read(StreamOf(AdcFixtures.WindowsSecurityCatalog())));
    }

    [Fact]
    public void Read_UnsupportedVersion_Throws()
    {
        Assert.Throws<AdcUnsupportedVersionException>(() =>
            AdcReader.Read(StreamOf(AdcFixtures.UnsupportedNested())));
    }

    [Fact]
    public void Read_HonorsCancellation()
    {
        using var stream = StreamOf(AdcFixtureWriter.Write(AdcFixtures.NestedCatalog()));
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => AdcReader.Read(stream, source.Token));
    }

    [Fact]
    public void Read_UnknownCategoryReference_Throws()
    {
        var catalog = AdcFixtures.EmptyCatalog();
        catalog.Volumes.Add(new AdcVolume { VolumeId = 1, Label = "VOL" });
        catalog.Items.Add(new AdcItem
        {
            ItemId = 1,
            ParentId = 0,
            VolumeId = 1,
            Kind = AdcItemKind.File,
            Name = "a.txt",
            CategoryIds = { 7 }
        });

        Assert.Throws<AdcCorruptException>(() => AdcReader.Read(StreamOf(AdcFixtureWriter.Write(catalog))));
    }

    [Fact]
    public void Read_UnknownCommentIndex_Throws()
    {
        var catalog = AdcFixtures.EmptyCatalog();
        catalog.Volumes.Add(new AdcVolume { VolumeId = 1, Label = "VOL" });
        catalog.Items.Add(new AdcItem
        {
            ItemId = 1,
            ParentId = 0,
            VolumeId = 1,
            Kind = AdcItemKind.File,
            Name = "a.txt",
            CommentIndex = 1
        });

        Assert.Throws<AdcCorruptException>(() => AdcReader.Read(StreamOf(AdcFixtureWriter.Write(catalog))));
    }

    private static MemoryStream StreamOf(byte[] bytes) => new(bytes, writable: false);

    private static int IndexOf(byte[] data, byte[] sequence)
    {
        for (var i = 0; i <= data.Length - sequence.Length; i++)
        {
            var match = true;
            for (var j = 0; j < sequence.Length; j++)
            {
                if (data[i + j] == sequence[j])
                {
                    continue;
                }

                match = false;
                break;
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)(data[offset] |
               (data[offset + 1] << 8) |
               (data[offset + 2] << 16) |
               (data[offset + 3] << 24));

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteUInt64(byte[] data, int offset, ulong value)
    {
        WriteUInt32(data, offset, (uint)value);
        WriteUInt32(data, offset + 4, (uint)(value >> 32));
    }
}
