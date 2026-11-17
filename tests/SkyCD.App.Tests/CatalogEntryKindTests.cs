using System.Collections.Generic;
using SkyCD.App.Views;
using SkyCD.Documents.Enum;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class CatalogEntryKindTests
{
    private static IReadOnlyList<Documents.CatalogDocument> ExtractRows(
        IEnumerable<Dictionary<string, object?>> rows)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<Documents.CatalogDocument>>(
            method.Invoke(null, [new List<Dictionary<string, object?>>(rows)]));
    }

    [Fact]
    public void EntryKind_MapsToMediaRatherThanFile()
    {
        var documents = ExtractRows([
            new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = "tellico:collection:0",
                ["parentId"] = null,
                ["kind"] = "media",
                ["name"] = "My Books",
                ["sizeBytes"] = 0L,
                ["properties"] = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase)
            },
            new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = "tellico:0:1",
                ["parentId"] = "tellico:collection:0",
                ["kind"] = "entry",
                ["name"] = "Dune",
                ["sizeBytes"] = 0L,
                ["properties"] = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase)
            }
        ]);

        Assert.Equal(CatalogDocumentType.Media, Assert.Single(documents, entry => entry.Name == "My Books").Type);
        Assert.Equal(CatalogDocumentType.Media, Assert.Single(documents, entry => entry.Name == "Dune").Type);
    }
}
