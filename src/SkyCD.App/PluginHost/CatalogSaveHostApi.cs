using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Couchbase;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Host.Catalog;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Presentation.Catalog;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.FileFormats;

namespace SkyCD.App.PluginHost;

/// <summary>
/// Writes plugin-provided slices of the open catalog through the file formats loaded right now, using the
/// same snapshot, loss assessment and staging write as Save As. The format manager is resolved per call
/// because plugin re-discovery replaces it.
/// </summary>
public sealed class CatalogSaveHostApi(
    RepositoryManager repositories,
    OpenCatalogState openCatalog,
    Func<PluginManager> pluginManager,
    Func<FileFormatManager> formats) : ICatalogSaveHostApi
{
    /// <inheritdoc />
    public IReadOnlyList<FileFormatDescriptor> GetRewritableFormats()
    {
        return formats().GetSaveFormats()
            .Where(static format => format.CanRead)
            .ToList();
    }

    /// <inheritdoc />
    public string ResolveRewritableFormatId(string? path)
    {
        var rewritable = GetRewritableFormats();
        if (rewritable.Count == 0)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                var resolved = formats().ResolveFormatId(null, path, forWrite: true);
                if (rewritable.Any(format =>
                        format.FormatId.Equals(resolved, StringComparison.OrdinalIgnoreCase)))
                {
                    return resolved;
                }
            }
            catch (Exception exception) when (exception is FileFormatHandlerResolutionException
                or UnsupportedFileFormatException)
            {
                // Fall through to the first rewritable format.
            }
        }

        return rewritable[0].FormatId;
    }

    /// <inheritdoc />
    public IReadOnlyList<FileFormatDataLossItem> CollectCatalogScopeItems()
    {
        return CatalogScopeDataService.Collect(
            ((VirtualCollectionDocumentRepository)repositories.For<VirtualCollectionDocument>()).GetOrdered(),
            ((StatusVariantDocumentRepository)repositories.For<StatusVariantDocument>()).GetOrdered(),
            ((ScanExclusionRuleDocumentRepository)repositories.For<ScanExclusionRuleDocument>()).GetOrdered(),
            ((AiScanExclusionDocumentRepository)repositories.For<AiScanExclusionDocument>()).GetOrdered(),
            pluginManager().GetCapabilities<ICatalogAuxiliaryDataProvider>());
    }

    /// <inheritdoc />
    public IReadOnlyList<FileFormatDataLossItem> AssessDataLoss(
        string formatId,
        string fileName,
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<FileFormatDataLossItem> catalogScopeItems)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(catalogScopeItems);
        var documents = ResolveDocuments(entries);
        return formats().AssessDataLoss(
            formatId, fileName, CatalogSaveSnapshot.BuildEntries(documents), catalogScopeItems);
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        string formatId,
        string fileName,
        Stream target,
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<FileFormatDataLossItem> catalogScopeItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(catalogScopeItems);
        var documents = ResolveDocuments(entries);
        await formats().WriteAsync(new FileFormatWriteRequest
        {
            FormatId = formatId,
            FileName = fileName,
            Target = target,
            Payload = CatalogSaveSnapshot.BuildRows(documents),
            CatalogScopeItems = catalogScopeItems
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task WriteFileAsync(
        string formatId,
        string path,
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<FileFormatDataLossItem> catalogScopeItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(catalogScopeItems);
        var documents = ResolveDocuments(entries);
        await formats().WriteFileAsync(
                formatId,
                path,
                CatalogSaveSnapshot.BuildRows(documents),
                cancellationToken: cancellationToken,
                catalogScopeItems: catalogScopeItems)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Maps the plugin's entry snapshot back to the stored documents, so the written rows keep the fields the
    /// neutral read model does not carry (status, child count, contents state).
    /// </summary>
    private IReadOnlyList<CatalogDocument> ResolveDocuments(IReadOnlyList<CatalogEntry> entries)
    {
        var repository = openCatalog.Repository;
        if (repository is null)
        {
            throw new CatalogSaveSourceChangedException();
        }

        var byId = repository.GetAll()
            .ToDictionary(static document => document.Id, StringComparer.Ordinal);
        var documents = new List<CatalogDocument>(entries.Count);
        foreach (var entry in entries)
        {
            if (!byId.TryGetValue(entry.Id, out var document))
            {
                throw new CatalogSaveSourceChangedException();
            }

            documents.Add(document);
        }

        return documents;
    }
}
