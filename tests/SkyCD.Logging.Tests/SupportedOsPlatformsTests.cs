using System;
using System.Linq;
using Xunit;

namespace SkyCD.Logging.Tests;

public sealed class SupportedOsPlatformsTests
{
    [Fact]
    public void Constants_AreNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(SupportedOsPlatforms.Android));
        Assert.False(string.IsNullOrWhiteSpace(SupportedOsPlatforms.FreeBsd));
        Assert.False(string.IsNullOrWhiteSpace(SupportedOsPlatforms.Ios));
        Assert.False(string.IsNullOrWhiteSpace(SupportedOsPlatforms.Linux));
        Assert.False(string.IsNullOrWhiteSpace(SupportedOsPlatforms.MacOs));
        Assert.False(string.IsNullOrWhiteSpace(SupportedOsPlatforms.TvOs));
        Assert.False(string.IsNullOrWhiteSpace(SupportedOsPlatforms.WatchOs));
        Assert.False(string.IsNullOrWhiteSpace(SupportedOsPlatforms.Windows));
    }

    [Fact]
    public void Constants_AreUnique()
    {
        var values = new[]
        {
            SupportedOsPlatforms.Android,
            SupportedOsPlatforms.FreeBsd,
            SupportedOsPlatforms.Ios,
            SupportedOsPlatforms.Linux,
            SupportedOsPlatforms.MacOs,
            SupportedOsPlatforms.TvOs,
            SupportedOsPlatforms.WatchOs,
            SupportedOsPlatforms.Windows
        };

        Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());
    }
}
