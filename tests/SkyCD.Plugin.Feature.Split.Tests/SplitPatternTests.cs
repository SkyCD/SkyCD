using SkyCD.Plugin.Feature.Split.Exceptions;
using SkyCD.Plugin.Feature.Split.Parsing;
using Xunit;

namespace SkyCD.Plugin.Feature.Split.Tests;

public class SplitPatternTests
{
    [Theory]
    [InlineData("part #N.scd", 1, 4, "gamez", "part #1.scd")]
    [InlineData("part #n.scd", 12, 12, "gamez", "part #12.scd")]
    [InlineData("disk {n} of {total}.scd", 3, 9, "gamez", "disk 3 of 9.scd")]
    [InlineData("{source}-#N.scd", 7, 7, "gamez", "gamez-#7.scd")]
    public void Build_ExpandsThePartTokens(string pattern, int partNumber, int partCount, string sourceName,
        string expected)
    {
        Assert.Equal(expected, SplitPattern.Build(SplitPattern.Normalize(pattern), partNumber, partCount, sourceName));
    }

    [Fact]
    public void Normalize_KeepsTheLegacyDefault()
    {
        Assert.Equal(SplitPattern.Default, SplitPattern.Normalize("  part #N.scd  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("catalog.scd")]
    [InlineData("part {total}.scd")]
    [InlineData("part #N")]
    [InlineData(@"parts\part #N.scd")]
    [InlineData("../part #N.scd")]
    [InlineData("/part #N.scd")]
    [InlineData("disk:part #N.scd")]
    [InlineData("part #N.con\trol.scd")]
    public void Normalize_RejectsPatternsThatCannotNamePartsSafely(string? pattern)
    {
        Assert.Throws<SplitPatternException>(() => SplitPattern.Normalize(pattern));
    }
}
