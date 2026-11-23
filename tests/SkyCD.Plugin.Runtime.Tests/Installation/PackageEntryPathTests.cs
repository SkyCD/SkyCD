using System;
using System.IO;
using PathRules = SkyCD.PackagePaths.PackageEntryPathRules;
using PackageEntryPath = SkyCD.PackagePaths.PackageEntryPath;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests.Installation;

/// <summary>Host-side package entry rules shared with the archive plugins.</summary>
public sealed class PackageEntryPathTests
{
    private static readonly PathRules SkipDot = PathRules.SkipCurrentDirectorySegments;

    [Theory]
    [InlineData(@"lib\net10.0\plugin.dll", "lib/net10.0/plugin.dll")]
    [InlineData("lib/net10.0/plugin.dll", "lib/net10.0/plugin.dll")]
    [InlineData(@"lib\net10.0\plugin.dll/", "lib/net10.0/plugin.dll")]
    [InlineData("  lib/net10.0/plugin.dll  ", "lib/net10.0/plugin.dll")]
    public void NormalizesBackslashesToForwardSlashes(string raw, string expected)
    {
        Assert.True(PackageEntryPath.TryNormalizeSafe(raw, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("./lib/net10.0/plugin.dll", "lib/net10.0/plugin.dll")]
    [InlineData("././lib/./plugin.dll", "lib/plugin.dll")]
    public void SkipsCurrentDirectorySegmentsOnlyWhenRequested(string raw, string expected)
    {
        Assert.True(PackageEntryPath.TryNormalize(raw, out var normalized, out var safe, SkipDot));
        Assert.Equal(expected, normalized);
        Assert.True(safe);

        Assert.True(PackageEntryPath.TryNormalize(raw, out _, out var defaultSafe));
        Assert.False(defaultSafe);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../outside.txt")]
    [InlineData("lib/../../outside.txt")]
    [InlineData(@"lib\..\outside.txt")]
    public void TraversalSegmentsAreUnsafe(string raw)
    {
        Assert.True(PackageEntryPath.TryNormalize(raw, out var normalized, out _));
        Assert.NotEqual(string.Empty, normalized);
        Assert.False(PackageEntryPath.TryNormalizeSafe(raw, out _));
    }

    [Theory]
    [InlineData("/etc/passwd", "etc/passwd")]
    [InlineData(@"C:\windows\system.dll", "C:/windows/system.dll")]
    [InlineData("C:/windows/system.dll", "C:/windows/system.dll")]
    public void AbsoluteAndDriveQualifiedPathsAreUnsafe(string raw, string expected)
    {
        Assert.True(PackageEntryPath.TryNormalize(raw, out var normalized, out var safe));
        Assert.Equal(expected, normalized);
        Assert.False(safe);
        Assert.False(PackageEntryPath.TryNormalizeSafe(raw, out _));
    }

    [Theory]
    [InlineData("lib/net10.0/evil\u0001.dll")]
    [InlineData("lib/net10.0/evil\u007F.dll")]
    public void ControlCharactersAreUnsafeOnlyWhenRejected(string raw)
    {
        Assert.True(PackageEntryPath.TryNormalize(raw, out var normalized, out _));
        Assert.Equal(raw, normalized);
        Assert.True(PackageEntryPath.TryNormalizeSafe(raw, out _));

        Assert.True(PackageEntryPath.TryNormalize(raw, out _, out var safe, PathRules.RejectControlCharacters));
        Assert.False(safe);
        Assert.False(PackageEntryPath.TryNormalizeSafe(raw, out _, PathRules.RejectControlCharacters));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyNamesAreRejected(string? raw)
    {
        Assert.False(PackageEntryPath.TryNormalize(raw, out var normalized, out var safe));
        Assert.Equal(string.Empty, normalized);
        Assert.False(safe);
        Assert.False(PackageEntryPath.TryNormalizeSafe(raw, out _));
    }

    [Fact]
    public void ResolvesInsideRootAndRejectsTraversalOutsideIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"skycd-entry-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Assert.True(PackageEntryPath.TryResolveUnderRoot(root, "lib/net10.0/plugin.dll", out var inside));
            Assert.Equal(Path.Combine(root, "lib", "net10.0", "plugin.dll"), inside);

            Assert.False(PackageEntryPath.TryResolveUnderRoot(root, "../outside.txt", out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
