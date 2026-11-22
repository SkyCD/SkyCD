using System.Linq;
using SkyCD.Plugin.FileFormat.LegacySkyCD;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.LegacyFormats.Tests;

public sealed class LegacyCatalogFormatRegistrationTests
{
    [Fact]
    public void SaveFormats_ExposeAllLegacySkyCDChoices()
    {
        var formats = CreateManager().GetSaveFormats();

        Assert.Equal(
            ["legacy-ascd", "legacy-cscd", "legacy-scd-binary", "legacy-scd"],
            formats.Select(format => format.FormatId).ToArray());
        Assert.Equal([".ascd"], formats[0].Extensions);
        Assert.Equal([".cscd"], formats[1].Extensions);
        Assert.Equal([".scd"], formats[2].Extensions);
        Assert.Equal([".scd"], formats[3].Extensions);
    }

    [Fact]
    public void OpenFormats_ExposeTheReadableLegacySkyCDChoices()
    {
        var formats = CreateManager().GetOpenFormats();

        Assert.Equal(
            ["legacy-ascd", "legacy-cscd", "legacy-scd"],
            formats.Select(format => format.FormatId).ToArray());
    }

    private static FileFormatManager CreateManager() =>
        new([new LegacyScdPlugin(), new LegacyScdBinaryPlugin(), new LegacyCscdPlugin(), new LegacyAscdPlugin()]);
}
