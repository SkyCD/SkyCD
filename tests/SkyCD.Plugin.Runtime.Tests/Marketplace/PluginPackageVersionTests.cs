using System.Collections.Generic;
using SkyCD.Plugin.Runtime.Marketplace.Models;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests.Marketplace;

public sealed class PluginPackageVersionTests
{
    [Theory]
    [InlineData("1.0.0")]
    [InlineData("2.0.0-beta.1")]
    [InlineData("2.0.0-beta.1+sha.abc")]
    [InlineData("1.2.3.4")]
    public void TryParse_AcceptsSemVerShapes(string text)
    {
        Assert.True(PluginPackageVersion.TryParse(text, out var version));
        Assert.Equal(text, version!.Original);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1..0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-beta..1")]
    [InlineData("1.0.0-beta_1")]
    [InlineData("x.y.z")]
    public void TryParse_RejectsMalformedVersions(string text)
    {
        Assert.False(PluginPackageVersion.TryParse(text, out _));
    }

    [Fact]
    public void Normalized_KeepsDottedPrereleaseAndDropsMetadata()
    {
        var version = PluginPackageVersion.Parse("2.0.0-Beta.1+sha.abc");

        Assert.Equal("2.0.0-Beta.1", version.Normalized);
        Assert.Equal("sha.abc", version.Metadata);
        Assert.True(version.IsPrerelease);
    }

    [Fact]
    public void StableOutranksItsPrerelease()
    {
        var stable = PluginPackageVersion.Parse("2.0.0");
        var prerelease = PluginPackageVersion.Parse("2.0.0-beta.1");
        var older = PluginPackageVersion.Parse("1.9.9");

        Assert.True(stable > prerelease);
        Assert.True(prerelease > older);
    }

    [Fact]
    public void PrereleaseLabelsCompareNumericallyBeforeTextually()
    {
        var versions = new List<PluginPackageVersion>
        {
            PluginPackageVersion.Parse("1.0.0-beta.10"),
            PluginPackageVersion.Parse("1.0.0-beta.2"),
            PluginPackageVersion.Parse("1.0.0-alpha"),
            PluginPackageVersion.Parse("1.0.0-1")
        };
        versions.Sort();

        Assert.Equal(
        [
            "1.0.0-1", "1.0.0-alpha", "1.0.0-beta.2", "1.0.0-beta.10"
        ], versions.ConvertAll(static version => version.Original));
    }

    [Fact]
    public void BuildMetadataIsIgnoredForIdentity()
    {
        var left = PluginPackageVersion.Parse("1.0.0+abc");
        var right = PluginPackageVersion.Parse("1.0.0+def");

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }
}
