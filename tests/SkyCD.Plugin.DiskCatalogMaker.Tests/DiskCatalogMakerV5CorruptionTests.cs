using System;
using System.Linq;
using SkyCD.Plugin.DiskCatalogMaker;
using SkyCD.Plugin.DiskCatalogMaker.Exceptions;
using Xunit;

namespace SkyCD.Plugin.DiskCatalogMaker.Tests;

public sealed class DiskCatalogMakerV5CorruptionTests
{
    private const int HeaderSize = DiskCatalogMakerV5Format.HeaderSize;
    private const int DirectoryEntrySize = DiskCatalogMakerV5Format.DirectoryEntrySize;
    private const int TableHeaderSize = DiskCatalogMakerV5Format.TableHeaderSize;

    [Fact]
    public void Parse_EmptyPayload_IsRejected()
    {
        AssertCorrupt([], "shorter than");
    }

    [Fact]
    public void Parse_TruncatedHeader_IsRejected()
    {
        AssertCorrupt(new byte[10], "shorter than");
    }

    [Fact]
    public void Parse_InvalidMagic_IsRejected()
    {
        var payload = BuildBaseCatalog();
        payload[0] = 0;

        AssertCorrupt(payload, "invalid magic");
    }

    [Fact]
    public void Parse_UnexpectedHeaderSize_IsRejected()
    {
        var payload = BuildBaseCatalog();
        DiskCatalogMakerV5FixtureBuilder.PatchUInt16(payload, 4, 24);

        AssertCorrupt(payload, "unsupported header size");
    }

    [Fact]
    public void Parse_UnsupportedMajorVersion_IsReportedSeparately()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder { MajorVersion = 6 };
        var exception = Assert.Throws<DiskCatalogMakerV5UnsupportedVersionException>(
            () => DiskCatalogMakerV5Parser.Parse(builder.Build()));

        Assert.Contains("6.0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("only format version 5", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WrongByteOrderMarker_IsRejected()
    {
        var payload = BuildBaseCatalog();
        DiskCatalogMakerV5FixtureBuilder.PatchUInt16(payload, 10, 0x4245);

        AssertCorrupt(payload, "byte-order marker");
    }

    [Fact]
    public void Parse_TableDirectoryInsideHeader_IsRejected()
    {
        var payload = BuildBaseCatalog();
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, 16, 8);

        AssertCorrupt(payload, "inside the catalog header");
    }

    [Fact]
    public void Parse_ExcessiveTableCount_IsRejected()
    {
        var payload = BuildBaseCatalog();
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, 20, 5000);

        AssertCorrupt(payload, "exceeds the supported maximum");
    }

    [Fact]
    public void Parse_DuplicateTableIdentifier_IsRejected()
    {
        var payload = BuildBaseCatalog();
        var secondEntry = HeaderSize + DirectoryEntrySize;
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, secondEntry, 3);

        AssertCorrupt(payload, "appears more than once");
    }

    [Fact]
    public void Parse_TableShorterThanHeader_IsRejected()
    {
        var payload = BuildBaseCatalog();
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, HeaderSize + 8, 4);

        AssertCorrupt(payload, "shorter than its");
    }

    [Fact]
    public void Parse_ExcessiveRecordCount_IsRejected()
    {
        var payload = BuildBaseCatalog();
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, HeaderSize + 12, 6_000_000);

        AssertCorrupt(payload, "exceeding the supported maximum");
    }

    [Fact]
    public void Parse_TableOffsetBeyondPayload_IsRejected()
    {
        var payload = BuildBaseCatalog();
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, HeaderSize + 4, (uint)payload.Length + 4);

        AssertCorrupt(payload, "requests");
    }

    [Fact]
    public void Parse_TableOffsetAboveIntRange_IsRejected()
    {
        var payload = BuildBaseCatalog();
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, HeaderSize + 4, 0x80000000);

        AssertCorrupt(payload, "exceeds the supported range");
    }

    [Fact]
    public void Parse_OverlappingTableRanges_AreRejected()
    {
        var payload = BuildBaseCatalog();
        var firstEntry = HeaderSize;
        var secondEntry = HeaderSize + DirectoryEntrySize;
        var firstOffset = DiskCatalogMakerV5FixtureBuilder.ReadUInt32(payload, firstEntry + 4);
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, secondEntry + 4, firstOffset);

        AssertCorrupt(payload, "overlap");
    }

    [Fact]
    public void Parse_TableOverlappingDirectory_IsRejected()
    {
        var payload = BuildBaseCatalog();
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, HeaderSize + 4, HeaderSize);

        AssertCorrupt(payload, "overlap");
    }

    [Fact]
    public void Parse_TruncatedTablePayload_IsRejected()
    {
        var payload = BuildBaseCatalog()[..^4];

        AssertCorrupt(payload, "requests");
    }

    [Fact]
    public void Parse_TableHeaderIdentifierMismatch_IsRejected()
    {
        var payload = BuildBaseCatalog();
        var strings = DiskCatalogMakerV5FixtureBuilder.FindTableOffset(payload, DiskCatalogMakerV5Format.VolumeNameTableId);
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, strings, 0x99);

        AssertCorrupt(payload, "does not match directory identifier");
    }

    [Fact]
    public void Parse_TableHeaderRecordCountMismatch_IsRejected()
    {
        var payload = BuildBaseCatalog();
        var strings = DiskCatalogMakerV5FixtureBuilder.FindTableOffset(payload, DiskCatalogMakerV5Format.VolumeNameTableId);
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, strings + 8, 999);

        AssertCorrupt(payload, "does not match directory count");
    }

    [Fact]
    public void Parse_TablePayloadLengthMismatch_IsRejected()
    {
        var payload = BuildBaseCatalog();
        var strings = DiskCatalogMakerV5FixtureBuilder.FindTableOffset(payload, DiskCatalogMakerV5Format.VolumeNameTableId);
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, strings + 12, 1);

        AssertCorrupt(payload, "declares payload length");
    }

    [Fact]
    public void Parse_UnexpectedKnownTableHeaderValue_IsRejected()
    {
        var payload = BuildBaseCatalog();
        var strings = DiskCatalogMakerV5FixtureBuilder.FindTableOffset(payload, DiskCatalogMakerV5Format.VolumeNameTableId);
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, strings + 4, 2);

        AssertCorrupt(payload, "unsupported header value");
    }

    [Fact]
    public void Parse_FixedRecordPayloadMismatch_IsRejected()
    {
        var payload = BuildBaseCatalog();
        var volumeTable = DiskCatalogMakerV5FixtureBuilder.FindTableOffset(payload, DiskCatalogMakerV5Format.VolumeDataTableId);
        var volumeEntry = HeaderSize + DirectoryEntrySize;
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, volumeEntry + 12, 2);
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, volumeTable + 8, 2);

        AssertCorrupt(payload, "declares 2 record(s)");
    }

    [Fact]
    public void Parse_StringLengthPastTableEnd_IsRejected()
    {
        var payload = BuildBaseCatalog();
        var strings = DiskCatalogMakerV5FixtureBuilder.FindTableOffset(payload, DiskCatalogMakerV5Format.VolumeNameTableId);
        var record = strings + TableHeaderSize;
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, record + 4, 10_000);

        AssertCorrupt(payload, "requests 10000 byte(s)");
    }

    [Fact]
    public void Parse_InvalidUtf8String_IsRejected()
    {
        var payload = BuildBaseCatalog();
        var strings = DiskCatalogMakerV5FixtureBuilder.FindTableOffset(payload, DiskCatalogMakerV5Format.VolumeNameTableId);
        payload[strings + TableHeaderSize + DiskCatalogMakerV5Format.VolumeNameRecordHeaderSize] = 0xFF;

        AssertCorrupt(payload, "not valid UTF-8");
    }

    [Fact]
    public void Parse_UnsupportedStringEncoding_IsRejected()
    {
        var payload = BuildBaseCatalog();
        var strings = DiskCatalogMakerV5FixtureBuilder.FindTableOffset(payload, DiskCatalogMakerV5Format.VolumeNameTableId);
        DiskCatalogMakerV5FixtureBuilder.PatchUInt16(
            payload, strings + TableHeaderSize + 8, 7);

        AssertCorrupt(payload, "unsupported encoding tag");
    }

    [Fact]
    public void Parse_OddUtf16ByteLength_IsRejected()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(
            1,
            "AB",
            nameEncoding: DiskCatalogMakerV5Format.Utf16LittleEndianEncoding);
        var payload = builder.Build();
        var strings = DiskCatalogMakerV5FixtureBuilder.FindTableOffset(payload, DiskCatalogMakerV5Format.VolumeNameTableId);
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, strings + TableHeaderSize + 4, 3);

        AssertCorrupt(payload, "odd byte length");
    }

    [Fact]
    public void Parse_DuplicateVolumeIdentifier_IsRejected()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "First");
        builder.AddVolume(1, "Second");

        AssertCorrupt(builder.Build(), "volume identifier 1 appears more than once");
    }

    [Fact]
    public void Parse_DuplicateItemIdentifier_IsRejected()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Disk");
        builder.AddItem(1, 2, null, "first.txt");
        builder.AddItem(1, 2, null, "second.txt");

        AssertCorrupt(builder.Build(), "item identifier 2 in volume 1 appears more than once");
    }

    [Fact]
    public void Parse_ReservedStringIdentifier_IsSkippedWithWarning()
    {
        var payload = BuildBaseCatalog();
        var strings = DiskCatalogMakerV5FixtureBuilder.FindTableOffset(payload, DiskCatalogMakerV5Format.VolumeNameTableId);
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, strings + TableHeaderSize, 0);

        var catalog = DiskCatalogMakerV5Parser.Parse(payload);

        Assert.Contains(catalog.Warnings,
            warning => warning.Contains("reserved identifier 0", StringComparison.Ordinal));
        Assert.Equal("Volume 1", Assert.Single(catalog.Volumes).Name);
    }

    [Fact]
    public void Parse_AliasExtraPastEnd_StopsAliasTableWithWarning()
    {
        var payload = BuildAliasCatalog();
        var aliases = DiskCatalogMakerV5FixtureBuilder.FindTableOffset(payload, DiskCatalogMakerV5Format.VolumeAliasTableId);
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, aliases + TableHeaderSize + 60, 1000);

        var catalog = DiskCatalogMakerV5Parser.Parse(payload);

        Assert.Empty(catalog.Aliases);
        Assert.Contains(catalog.Warnings,
            warning => warning.Contains("points past the end", StringComparison.Ordinal));
        Assert.Contains(catalog.Warnings,
            warning => warning.Contains("only 0 could be read", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_AliasExtraLengthAboveIntRange_StopsAliasTableWithWarning()
    {
        var payload = BuildAliasCatalog();
        var aliases = DiskCatalogMakerV5FixtureBuilder.FindTableOffset(payload, DiskCatalogMakerV5Format.VolumeAliasTableId);
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, aliases + TableHeaderSize + 60, uint.MaxValue);

        var catalog = DiskCatalogMakerV5Parser.Parse(payload);

        Assert.Empty(catalog.Aliases);
        Assert.Contains(catalog.Warnings,
            warning => warning.Contains("unsupported extra length", StringComparison.Ordinal));
    }

    private static byte[] BuildBaseCatalog()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Disk");
        builder.AddItem(1, 2, null, "file.txt", sizeBytes: 10);
        return builder.Build();
    }

    private static byte[] BuildAliasCatalog()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Disk");
        builder.AddItem(1, 2, null, "alias", DiskCatalogMakerV5ItemKind.Alias);
        builder.AddAlias(9, 2, extraBytes: [1, 2, 3, 4]);
        return builder.Build();
    }

    private static void AssertCorrupt(byte[] payload, string expectedFragment)
    {
        var exception = Assert.Throws<DiskCatalogMakerV5CorruptCatalogException>(
            () => DiskCatalogMakerV5Parser.Parse(payload, "fixture"));
        Assert.Contains(expectedFragment, exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
