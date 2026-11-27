using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Presentation.Catalog;

namespace SkyCD.App;

/// <summary>
/// What a save would lose in the routed format, used to drive the confirmation dialog.
/// </summary>
public sealed record CatalogSavePreview(string FormatDisplayName, IReadOnlyList<FileFormatDataLossItem> Items);

public sealed class CatalogSaveService(FileFormatManager formats)
{
    /// <summary>
    /// Assesses loss for the target path, asks for confirmation when anything would be lost, and only
    /// then writes. Declining leaves the target file untouched.
    /// </summary>
    public async Task<bool> SaveAsync(
        string path,
        IReadOnlyList<CatalogDocument> documents,
        IReadOnlyList<FileFormatDataLossItem> catalogScopeItems,
        Func<CatalogSavePreview, Task<bool>> confirmLoss,
        CancellationToken cancellationToken = default)
    {
        var formatId = formats.ResolveFormatId(null, path, forWrite: true);
        var rows = CatalogSaveSnapshot.BuildRows(documents);
        var entries = CatalogSaveSnapshot.BuildEntries(documents);

        var loss = formats.AssessDataLoss(formatId, System.IO.Path.GetFileName(path), entries, catalogScopeItems);
        if (loss.Count > 0)
        {
            var displayName = formats.GetSaveFormats()
                .FirstOrDefault(format => format.FormatId.Equals(formatId, StringComparison.OrdinalIgnoreCase))
                ?.DisplayName ?? formatId;
            if (!await confirmLoss(new CatalogSavePreview(displayName, loss)))
            {
                return false;
            }
        }

        await formats.WriteFileAsync(formatId, path, rows, cancellationToken: cancellationToken,
            catalogScopeItems: catalogScopeItems);
        return true;
    }
}
