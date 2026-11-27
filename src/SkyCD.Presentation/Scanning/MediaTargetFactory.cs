using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Derives the media target of any catalog root and applies captured values to it, so every scan flow hands
/// capabilities the same description of the medium instead of a drive-only one. A root can be a mounted
/// volume, an optical disc, a folder, a single file, a network location or a folder filled by a plugin source.
/// </summary>
public static class MediaTargetFactory
{
    private const string DriveTypeKey = "DriveType";
    private const string RootDirectoryKey = "RootDirectory";
    private const string FullNameKey = "FullName";
    private const string UrlKey = "URL";

    /// <summary>Describes the medium of an existing catalog root.</summary>
    public static MediaTarget FromCatalogRoot(CatalogDocument root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new MediaTarget(KindOf(root), LocationOf(root), root.Name, FactsOf(root.Properties));
    }

    /// <summary>Describes an enumerated drive; a CD-ROM drive is an optical disc even when it exposes a filesystem.</summary>
    public static MediaTarget FromDrive(MediaDriveInfo drive, string? name,
        IReadOnlyDictionary<string, string?>? facts = null)
    {
        ArgumentNullException.ThrowIfNull(drive);
        var kind = drive.DriveType == DriveType.CDRom ? MediaTargetKind.OpticalDisc : MediaTargetKind.Volume;
        return new MediaTarget(kind, drive.RootDirectory, name, facts);
    }

    /// <summary>Snapshots catalog root properties as invariant, read-only target facts.</summary>
    public static IReadOnlyDictionary<string, string?>? FactsOf(PropertiesCollection properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (properties.Count == 0)
        {
            return null;
        }

        var facts = new Dictionary<string, string?>(properties.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in properties)
        {
            facts[pair.Key] = pair.Value switch
            {
                null => null,
                string text => text,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => pair.Value.ToString()
            };
        }

        return facts;
    }

    /// <summary>
    /// Captures the target and writes the first value of each key into the root properties. Capture is
    /// best-effort enrichment: plugin failures are reported at the provider boundary and never break a scan.
    /// </summary>
    public static void Capture(PropertiesCollection properties, IMediaTargetMetadataProvider? provider,
        MediaTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(target);
        if (provider is null)
        {
            return;
        }

        try
        {
            // Traversal is synchronous, so scans already run on a worker thread; waiting here keeps captured
            // values on the scan result instead of publishing a second time.
            var captures = provider.CaptureAsync(target, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var capture in captures)
            {
                foreach (var value in capture.Capture.Values)
                {
                    var text = FormatValue(value);
                    if (text is not null && seenKeys.Add(value.Key))
                    {
                        properties[value.Key] = text;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Media metadata is optional; a broken plugin must not fail the surrounding scan.
        }
    }

    private static MediaTargetKind KindOf(CatalogDocument root) => root.Type switch
    {
        CatalogDocumentType.File => MediaTargetKind.File,
        CatalogDocumentType.NetworkFolder => MediaTargetKind.NetworkFolder,
        CatalogDocumentType.NetworkResource => MediaTargetKind.NetworkResource,
        _ => DriveOrFolderKind(root)
    };

    private static MediaTargetKind DriveOrFolderKind(CatalogDocument root)
    {
        if (string.Equals(Text(root.Properties, DriveTypeKey), nameof(DriveType.CDRom),
                StringComparison.OrdinalIgnoreCase))
        {
            return MediaTargetKind.OpticalDisc;
        }

        if (root.Properties.ContainsKey(DriveTypeKey) || root.Properties.ContainsKey(RootDirectoryKey))
        {
            return MediaTargetKind.Volume;
        }

        var location = LocationOf(root);
        if (string.IsNullOrWhiteSpace(location))
        {
            // A folder produced by an account catalog or a capture source factory has no address to read.
            return MediaTargetKind.SourceFolder;
        }

        return IsRemote(location) ? MediaTargetKind.NetworkFolder : MediaTargetKind.Folder;
    }

    private static string? LocationOf(CatalogDocument root) =>
        Text(root.Properties, RootDirectoryKey)
        ?? Text(root.Properties, FullNameKey)
        ?? Text(root.Properties, UrlKey);

    private static bool IsRemote(string location) =>
        location.StartsWith(@"\\", StringComparison.Ordinal)
        || location.StartsWith("//", StringComparison.Ordinal)
        || location.Contains("://", StringComparison.Ordinal);

    private static string? Text(PropertiesCollection properties, string key) =>
        properties.TryGetValue(key, out var value) && value is not null ? value.ToString() : null;

    private static string? FormatValue(MediaMetadataValue value) => value.Type switch
    {
        MediaMetadataType.Text => value.Text,
        MediaMetadataType.Integer => value.Integer?.ToString(CultureInfo.InvariantCulture),
        MediaMetadataType.Number => value.Number?.ToString(CultureInfo.InvariantCulture),
        MediaMetadataType.Boolean => value.Boolean?.ToString(),
        MediaMetadataType.Timestamp => value.Timestamp?.ToString("O", CultureInfo.InvariantCulture),
        _ => null
    };
}
