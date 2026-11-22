using System;
using System.Text;
using SkyCD.Plugin.Metadata.Image.Parsing;
using Xunit;

namespace SkyCD.Plugin.Metadata.Image.Tests;

/// <summary>
/// Exercises the MetadataExtractor mapping directly against hand-built containers: which carrier wins when
/// several carry the same field, and how EXIF text encodings reach the catalog.
/// </summary>
public sealed class EmbeddedMetadataReaderTests
{
    [Fact]
    public void OriginalDate_WithOffsetCompanion_KeepsTheOffset()
    {
        var data = MetadataFixtures.TiffWithSubIfd(littleEndian: true,
            [],
            [
                MetadataFixtures.Ascii(0x9003, "2021:06:05 18:30:00"),
                MetadataFixtures.Ascii(0x9011, "+02:00")
            ]);

        var captured = EmbeddedMetadataReader.Read(data).CapturedAt;

        Assert.Equal(new DateTimeOffset(2021, 6, 5, 18, 30, 0, TimeSpan.FromHours(2)), captured);
    }

    [Fact]
    public void OriginalDate_BeatsTheIfd0FallbackEvenWhenWrittenLater()
    {
        var data = MetadataFixtures.TiffWithSubIfd(littleEndian: true,
            [MetadataFixtures.Ascii(0x0132, "2022:01:01 09:00:00")],
            [MetadataFixtures.Ascii(0x9003, "2021:06:05 18:30:00")]);

        var captured = EmbeddedMetadataReader.Read(data).CapturedAt;

        Assert.Equal(new DateTimeOffset(2021, 6, 5, 18, 30, 0, TimeSpan.Zero), captured);
    }

    [Fact]
    public void Ifd0Date_IsUsedWhenNoOriginalTimestampExists()
    {
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Ascii(0x0132, "2020:03:04 05:06:07"));

        var captured = EmbeddedMetadataReader.Read(data).CapturedAt;

        Assert.Equal(new DateTimeOffset(2020, 3, 4, 5, 6, 7, TimeSpan.Zero), captured);
    }

    [Fact]
    public void UnreadableDates_ProduceNoTimestamp()
    {
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Ascii(0x0132, "not a date"));

        Assert.Null(EmbeddedMetadataReader.Read(data).CapturedAt);
    }

    [Fact]
    public void XmpCaptionAndRating_WinOverTheExifDirectory()
    {
        var packet = Encoding.UTF8.GetBytes(MetadataFixtures.Xmp(
            keywords: ["from xmp"], description: "XMP caption", rating: 5));
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Ascii(0x010E, "EXIF description"),
            MetadataFixtures.Utf16(0x9C9E, "from xp", littleEndian: true),
            MetadataFixtures.Short(0x4746, 1, littleEndian: true),
            MetadataFixtures.Raw(0x02BC, packet));

        var scan = EmbeddedMetadataReader.Read(data).Tags;

        Assert.Equal("XMP caption", scan.Comment);
        Assert.Equal(5, scan.Rating);
        Assert.Equal(["from xmp", "from xp"], scan.Keywords);
    }

    [Fact]
    public void WindowsXpComment_IsDecodedAsACaption()
    {
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Utf16(0x9C9C, "Caption written by Explorer", littleEndian: true));

        Assert.Equal("Caption written by Explorer", EmbeddedMetadataReader.Read(data).Tags.Comment);
    }

    [Fact]
    public void ExifUserComment_IsDecodedAsACaption()
    {
        var payload = new byte[8 + 12];
        Encoding.ASCII.GetBytes("UNICODE\0").CopyTo(payload, 0);
        Encoding.BigEndianUnicode.GetBytes("Sunset").CopyTo(payload, 8);
        var data = MetadataFixtures.Tiff(littleEndian: true, MetadataFixtures.Raw(0x9286, payload, type: 7));

        Assert.Equal("Sunset", EmbeddedMetadataReader.Read(data).Tags.Comment);
    }

    [Fact]
    public void IntegerRating_BeatsContainerTextThroughTheShellScale()
    {
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Short(0x4749, 50, littleEndian: true));

        Assert.Equal(3, EmbeddedMetadataReader.Read(data).Tags.Rating);
    }
}
