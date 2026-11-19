using System;
using System.IO;
using SkyCD.Formatting;
using Xunit;

namespace SkyCD.Formatting.Tests;

public class FileAttributesFormattingTests
{
    [Fact]
    public void FormatFileAttributes_OneDriveMask_ListsFlagsInAscendingBitOrder()
    {
        var text = FileAttributesFormatting.FormatFileAttributes(5242912, _ => null);

        Assert.Equal("Archive, Unpinned, Recall on data access", text);
    }

    [Fact]
    public void FormatFileAttributes_Zero_ReturnsLocalizedNone()
    {
        var text = FileAttributesFormatting.FormatFileAttributes(0, key => key == "AttributeNone" ? "Nėra" : null);

        Assert.Equal("Nėra", text);
    }

    [Theory]
    [InlineData(0x00000001u, "Read-only")]
    [InlineData(0x00000080u, "Normal")]
    [InlineData(0x00004000u, "Encrypted")]
    [InlineData(0x00020000u, "No scrub data")]
    [InlineData(0x00040000u, "Recall on open")]
    [InlineData(0x00080000u, "Pinned")]
    [InlineData(0x00100000u, "Unpinned")]
    [InlineData(0x00200000u, "Strictly sequential")]
    [InlineData(0x00400000u, "Recall on data access")]
    public void FormatFileAttributes_SingleFlag_ReturnsEnglishName(uint mask, string expected)
    {
        var text = FileAttributesFormatting.FormatFileAttributes(mask, _ => null);

        Assert.Equal(expected, text);
    }

    [Fact]
    public void FormatFileAttributes_CombinedKnownAndUnknownBits_AppendsHexRemainder()
    {
        var text = FileAttributesFormatting.FormatFileAttributes(0x00800020, _ => null);

        Assert.Equal("Archive, 0x800000", text);
    }

    [Fact]
    public void FormatFileAttributes_OnlyUnknownBits_ReturnsHexRemainder()
    {
        var text = FileAttributesFormatting.FormatFileAttributes(0x80000000, _ => null);

        Assert.Equal("0x80000000", text);
    }

    [Fact]
    public void FormatFileAttributes_ResolveName_UsesLocalizedNames()
    {
        var text = FileAttributesFormatting.FormatFileAttributes(
            5242912,
            key => key switch
            {
                "AttributeArchive" => "Archyvinis",
                "AttributeUnpinned" => "Neprisegtas",
                "AttributeRecallOnDataAccess" => "Atsisiunčiama skaitant duomenis",
                _ => null,
            });

        Assert.Equal("Archyvinis, Neprisegtas, Atsisiunčiama skaitant duomenis", text);
    }

    [Theory]
    [InlineData(5242912, 5242912u)]
    [InlineData(32, 32u)]
    [InlineData(0, 0u)]
    public void TryGetMask_NumericValues_ReturnMask(long value, uint expected)
    {
        Assert.True(FileAttributesFormatting.TryGetMask(value, out var mask));
        Assert.Equal(expected, mask);
    }

    [Theory]
    [InlineData((int)5242912, 5242912u)]
    [InlineData((uint)16, 16u)]
    [InlineData((ulong)0x400000, 0x400000u)]
    public void TryGetMask_IntegralClrTypes_ReturnMask(object value, uint expected)
    {
        Assert.True(FileAttributesFormatting.TryGetMask(value, out var mask));
        Assert.Equal(expected, mask);
    }

    [Theory]
    [InlineData("5242912", 5242912u)]
    [InlineData("  32 ", 32u)]
    [InlineData("0", 0u)]
    public void TryGetMask_DecimalStrings_ReturnMask(string value, uint expected)
    {
        Assert.True(FileAttributesFormatting.TryGetMask(value, out var mask));
        Assert.Equal(expected, mask);
    }

    [Fact]
    public void TryGetMask_FileAttributesEnum_ReturnMask()
    {
        Assert.True(FileAttributesFormatting.TryGetMask(FileAttributes.Archive | FileAttributes.ReadOnly, out var mask));
        Assert.Equal(0x21u, mask);
    }

    [Theory]
    [InlineData("ReadOnly, Archive")]
    [InlineData("Archive, ReparsePoint")]
    [InlineData("Archive")]
    [InlineData("0x20")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-1")]
    [InlineData("12.5")]
    public void TryGetMask_NonMaskText_ReturnsFalse(string value)
    {
        Assert.False(FileAttributesFormatting.TryGetMask(value, out var mask));
        Assert.Equal(0u, mask);
    }

    [Fact]
    public void TryGetMask_NullAndUnsupportedTypes_ReturnFalse()
    {
        Assert.False(FileAttributesFormatting.TryGetMask(null, out _));
        Assert.False(FileAttributesFormatting.TryGetMask(1.5d, out _));
        Assert.False(FileAttributesFormatting.TryGetMask(DateTime.UtcNow, out _));
    }
}
