using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SkyCD.Documents;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Computes the stable source identity that keys cached extraction results. Fingerprints are built
/// from catalog-relative facts only, so they survive moving the catalog database to another machine
/// and stay valid while the original media is disconnected. Absolute paths are never included.
/// </summary>
public static class MediaSourceFingerprint
{
    private const string Version = "v1";

    /// <summary>Builds a fingerprint from a source scope, a scope-relative path and source facts.</summary>
    public static string Create(string scope, string relativePath, long length, DateTimeOffset? lastModifiedUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var canonical = string.Join(
            '|',
            Version,
            scope.Trim(),
            NormalizeRelativePath(relativePath),
            length.ToString(CultureInfo.InvariantCulture),
            lastModifiedUtc?.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture) ?? "-");

        return MediaCacheHashing.Sha256Hex(canonical);
    }

    /// <summary>
    /// Builds the fingerprint of a catalog entry. The scope is the entry's root ancestor (the media or
    /// folder root it was scanned into), which <see cref="SkyCD.Presentation.Staging.CatalogEntryMerger"/>
    /// keeps stable across rescans, and the path is relative to that root.
    /// </summary>
    public static string ForCatalogEntry(CatalogDocument entry, IReadOnlyList<CatalogDocument>? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var chain = ResolveAncestorChain(entry, catalog);
        var scope = chain.Count > 0 ? chain[0].Id : entry.Id;
        var relativePath = string.Join(
            '/',
            (chain.Count > 0 ? chain.Skip(1) : Enumerable.Empty<CatalogDocument>())
            .Select(document => document.Name)
            .Append(entry.Name));

        return Create(scope, relativePath, entry.Size, ReadLastModifiedUtc(entry));
    }

    /// <summary>The file extension of a catalog entry, including the leading dot, or an empty string.</summary>
    public static string GetExtension(CatalogDocument entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var extension = Path.GetExtension(entry.Name);
        return string.IsNullOrWhiteSpace(extension) ? string.Empty : extension;
    }

    /// <summary>The last write time recorded by the scanner, when extended info was captured.</summary>
    public static DateTimeOffset? ReadLastModifiedUtc(CatalogDocument entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!entry.Properties.TryGetValue("LastWriteTime", out var raw) || raw is null)
        {
            return null;
        }

        var text = raw.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static IReadOnlyList<CatalogDocument> ResolveAncestorChain(
        CatalogDocument entry,
        IReadOnlyList<CatalogDocument>? catalog)
    {
        if (catalog is null || catalog.Count == 0 || string.IsNullOrWhiteSpace(entry.ParentId))
        {
            return [];
        }

        var byId = new Dictionary<string, CatalogDocument>(StringComparer.Ordinal);
        foreach (var document in catalog)
        {
            byId.TryAdd(document.Id, document);
        }

        var chain = new List<CatalogDocument>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { entry.Id };
        var parentId = entry.ParentId;

        while (!string.IsNullOrWhiteSpace(parentId) &&
               byId.TryGetValue(parentId, out var parent) &&
               visited.Add(parent.Id))
        {
            chain.Insert(0, parent);
            parentId = parent.ParentId;
        }

        return chain;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        var normalized = relativePath.Replace('\\', '/').Trim('/');
        return OperatingSystem.IsWindows() ? normalized.ToLowerInvariant() : normalized;
    }
}
