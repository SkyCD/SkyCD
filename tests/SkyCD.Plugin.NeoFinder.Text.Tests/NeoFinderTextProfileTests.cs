using Xunit;

namespace SkyCD.Plugin.NeoFinder.Text.Tests;

public sealed class NeoFinderTextProfileTests
{
    [Theory]
    [InlineData("Name", NeoFinderTextColumn.Name)]
    [InlineData("Kind", NeoFinderTextColumn.Kind)]
    [InlineData("Size", NeoFinderTextColumn.Size)]
    [InlineData("Where", NeoFinderTextColumn.Path)]
    [InlineData("Path", NeoFinderTextColumn.Path)]
    [InlineData("Volume", NeoFinderTextColumn.Volume)]
    [InlineData("Catalog", NeoFinderTextColumn.Catalog)]
    [InlineData("Date Modified", NeoFinderTextColumn.Modified)]
    [InlineData("Comments", NeoFinderTextColumn.Comments)]
    [InlineData("Label", NeoFinderTextColumn.Label)]
    [InlineData("Pfad", NeoFinderTextColumn.Path)]
    [InlineData("Größe", NeoFinderTextColumn.Size)]
    [InlineData("Emplacement", NeoFinderTextColumn.Path)]
    [InlineData("Tamaño", NeoFinderTextColumn.Size)]
    [InlineData("  Name  ", NeoFinderTextColumn.Name)]
    [InlineData("Thumbnail", NeoFinderTextColumn.Unknown)]
    [InlineData("Checksum", NeoFinderTextColumn.Unknown)]
    [InlineData("", NeoFinderTextColumn.Unknown)]
    public void MapColumn_ResolvesAliases(string header, NeoFinderTextColumn expected)
    {
        Assert.Equal(expected, NeoFinderTextProfile.MapColumn(header));
    }
}
