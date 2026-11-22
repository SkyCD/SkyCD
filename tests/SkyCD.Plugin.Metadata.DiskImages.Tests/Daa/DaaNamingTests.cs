using System;
using System.IO;
using SkyCD.Plugin.Metadata.DiskImages.Daa.Models;
using SkyCD.Plugin.Metadata.DiskImages.Models;
using Xunit;

namespace SkyCD.Plugin.Metadata.DiskImages.Tests;

public sealed class DaaEntryPathTests
{
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("folder/../../escape.txt")]
    [InlineData("..")]
    [InlineData(" ")]
    [InlineData("")]
    public void TryNormalize_RejectsUnsafePaths(string path)
    {
        Assert.False(DiskImageEntryPath.TryNormalizeDaa(path, out _));
    }

    [Fact]
    public void TryNormalize_RejectsControlCharacters()
    {
        Assert.False(DiskImageEntryPath.TryNormalizeDaa("folder/bad\u0001name.txt", out _));
    }

    [Fact]
    public void TryNormalize_StripsVersionAndUppercases()
    {
        Assert.True(DiskImageEntryPath.TryNormalizeDaa("root/deep/file.txt;1", out var normalized));
        Assert.Equal("ROOT/DEEP/FILE.TXT", normalized);
    }
}

public sealed class DaaVolumeNamingTests
{
    [Fact]
    public void PartNumberedName_UsesSameWidthForCompanions()
    {
        var naming = DaaVolumeNaming.Create(Path.Combine("music", "image.part01.daa"));

        Assert.NotNull(naming);
        Assert.Equal(Path.Combine("music", "image.part02.daa"), naming!.GetPath(1));
    }

    [Fact]
    public void PlainName_UsesDnnCompanionSuffix()
    {
        var naming = DaaVolumeNaming.Create(Path.Combine("music", "image.daa"));

        Assert.NotNull(naming);
        Assert.Equal(Path.Combine("music", "image.d01"), naming!.GetPath(1));
    }

    [Fact]
    public void NonDaaName_ReturnsNull()
    {
        Assert.Null(DaaVolumeNaming.Create("image.zip"));
    }
}
