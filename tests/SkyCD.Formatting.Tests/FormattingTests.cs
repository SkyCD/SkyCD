using System;
using SkyCD.Formatting;
using Xunit;

namespace SkyCD.Formatting.Tests;

public class FormattingTests
{
    [Fact]
    public void FormatBytes_ReturnsHumanizedText_WithOptionalSpaceRemoval()
    {
        var withSpace = SizeFormatting.FormatBytes(1536);
        var withoutSpace = SizeFormatting.FormatBytes(1536, removeSpace: true);

        Assert.Equal("1.5 KB", withSpace);
        Assert.Equal("1.5KB", withoutSpace);
    }

    [Theory]
    [InlineData(1000, "1000 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    public void FormatAboutDialogBytes_UsesExpectedPrecision(long bytes, string expected)
    {
        Assert.Equal(expected, SizeFormatting.FormatAboutDialogBytes(bytes));
    }

    [Theory]
    [InlineData("1 KB", 1024)]
    [InlineData("1KB", 1024)]
    [InlineData("2048", 2048)]
    [InlineData(" 2 MB ", 2097152)]
    public void TryParseBytes_AcceptsSupportedFormats(string input, long expectedBytes)
    {
        var success = SizeFormatting.TryParseBytes(input, out var bytes);

        Assert.True(success);
        Assert.Equal(expectedBytes, bytes);
    }

    [Fact]
    public void TryParseBytes_InvalidInput_ReturnsFalseAndZero()
    {
        var success = SizeFormatting.TryParseBytes("not-a-size", out var bytes);

        Assert.False(success);
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void FormatAboutDialogDuration_NegativeValue_IsClampedToZero()
    {
        var text = TimeFormatting.FormatAboutDialogDuration(TimeSpan.FromMinutes(-1));

        Assert.Equal("0 seconds", text);
    }

    [Fact]
    public void FormatAboutDialogDuration_UsesCompositeUnits_ForLongDurations()
    {
        var text = TimeFormatting.FormatAboutDialogDuration(
            TimeSpan.FromDays(1) + TimeSpan.FromHours(2) + TimeSpan.FromMinutes(3));

        Assert.Equal("1 day 2 hours 3 minutes", text);
    }
}
