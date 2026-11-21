using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

public class MnemonicTextTests
{
    [Theory]
    [InlineData("Type", "Type")]
    [InlineData("_Type", "Type")]
    [InlineData("&Status", "Status")]
    [InlineData("_Tipas", "Tipas")]
    [InlineData("_B__sena", "Bsena")]
    [InlineData("Save _As...", "Save As...")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Strip_RemovesAccessKeyMarkers(string? input, string expected)
    {
        Assert.Equal(expected, MnemonicText.Strip(input));
    }
}
