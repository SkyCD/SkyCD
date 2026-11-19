using System;
using System.Collections.Generic;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Maps WebDAV multistatus resources onto the catalog's <see cref="RemoteEntry"/> (#625).
/// The 207 Multi-Status parsing belongs to WebDav.Client; href resolution, percent-decoding
/// and collection semantics are applied here before the entries reach the shared traversal.
/// </summary>
public static class WebDavRemoteListingMapper
{
    public static IReadOnlyList<RemoteEntry> Map(
        IEnumerable<WebDav.WebDavResource?>? resources,
        string directoryPath,
        Uri requestUri)
    {
        var entries = new List<RemoteEntry>();
        if (resources is null)
        {
            return entries;
        }

        var directory = FtpPath.Normalize(directoryPath);
        foreach (var resource in resources)
        {
            var entry = MapItem(resource, directory, requestUri);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>
    /// Projects one multistatus response. Returns null for the collection's own record, the
    /// protocol self/parent records, and anything that is not a direct member of the listed
    /// collection (Depth: 1 members are the collection path plus one segment).
    /// </summary>
    public static RemoteEntry? MapItem(WebDav.WebDavResource? resource, string directoryPath, Uri requestUri)
    {
        if (resource is null || string.IsNullOrWhiteSpace(resource.Uri))
        {
            return null;
        }

        var rawHref = resource.Uri.Trim().TrimEnd('/');
        if (rawHref.Length == 0)
        {
            return null;
        }

        var rawSlash = rawHref.LastIndexOf('/');
        var rawName = rawSlash < 0 ? rawHref : rawHref[(rawSlash + 1)..];
        if (rawName is "." or "..")
        {
            return null;
        }

        // Hrefs may be relative ("file.txt", "sub/") or absolute ("/pub/docs/file.txt") and are
        // usually percent-encoded; resolve against the request URI before taking the name.
        if (!Uri.TryCreate(requestUri, resource.Uri, out var resolved))
        {
            return null;
        }

        string decodedPath;
        try
        {
            decodedPath = Uri.UnescapeDataString(resolved.AbsolutePath);
        }
        catch (UriFormatException)
        {
            return null;
        }

        var itemPath = FtpPath.Normalize(decodedPath);
        var directory = FtpPath.Normalize(directoryPath);
        if (itemPath == directory)
        {
            // Depth: 1 always reports the collection itself first.
            return null;
        }

        var slash = itemPath.LastIndexOf('/');
        var parent = slash <= 0 ? "/" : itemPath[..slash];
        if (parent != directory)
        {
            return null;
        }

        var name = slash < 0 ? itemPath : itemPath[(slash + 1)..];
        if (!IsCatalogName(name))
        {
            return null;
        }

        var isDirectory = resource.IsCollection || resolved.AbsolutePath.EndsWith('/');
        var size = isDirectory ? 0 : Math.Max(0, resource.ContentLength ?? 0);

        return new RemoteEntry(
            name,
            itemPath,
            directory,
            isDirectory,
            size,
            null,
            ModifiedUtc(resource));
    }

    /// <summary>The resource timestamp normalized to UTC, or null when the server did not report one.</summary>
    private static DateTime? ModifiedUtc(WebDav.WebDavResource resource)
    {
        var modified = resource.LastModifiedDate;
        if (modified is null || modified == DateTime.MinValue)
        {
            return null;
        }

        return modified.Value.Kind switch
        {
            DateTimeKind.Utc => modified,
            DateTimeKind.Local => modified.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(modified.Value, DateTimeKind.Utc),
        };
    }

    private static bool IsCatalogName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name is not "." and not ".." &&
        name.IndexOfAny(['/', '\\', '\0']) < 0;
}
