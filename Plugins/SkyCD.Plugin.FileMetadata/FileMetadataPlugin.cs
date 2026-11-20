using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.FileMetadata;

/// <summary>
/// Publishes the keywords, hierarchical tags, comment and rating that travel inside a file, so the catalog
/// keeps the metadata a user already assigned in Windows Explorer, Lightroom, digiKam, Photo Mechanic or an
/// XMP sidecar. Sources without any of these tags yield an empty result instead of a failure, and each field
/// has its own option, matching the reference options dialog.
/// </summary>
/// <remarks>
/// The source is read once, bounded, and discarded; nothing is written back. Metadata stored outside the
/// file — macOS Finder tags in extended attributes, Linux gvfs/Tracker databases, Windows shell properties
/// that live in sidecar stores rather than the file — is out of reach through the host's path-less
/// <see cref="IMediaSource"/> and is therefore not claimed. See <c>docs/plugins/file-metadata.md</c>.
/// </remarks>
public sealed class FileMetadataPlugin : IMediaMetadataCapability, IPluginSettingsConsumer
{
    private const int ProviderPriority = 10;
    private const int ChunkBytes = 256 * 1024;

    private readonly FileMetadataOptions options;
    private volatile FileMetadataSettings settings = FileMetadataSettings.Default;

    public FileMetadataPlugin()
        : this(FileMetadataOptions.Default)
    {
    }

    public FileMetadataPlugin(FileMetadataOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor
    {
        get
        {
            var current = settings;
            return current.CollectionEnabled
                ? new MediaDescriptor(current.Extensions, FileMetadataSchema.ContentTypes, FileMetadataSchema.Schema,
                    Priority: ProviderPriority)
                : new MediaDescriptor([], [], FileMetadataSchema.Schema, Priority: ProviderPriority);
        }
    }

    public void ApplySettings(IReadOnlyDictionary<string, string?> settings) =>
        this.settings = FileMetadataSettings.From(settings);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var current = settings;
        if (!current.CollectionEnabled)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult([]);
        }

        var length = source.Length > 0 ? source.Length : item.Length;
        if (length <= 0)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult([]);
        }

        var data = await ReadPrefixAsync(source, (int)Math.Min(length, options.MaxScanBytes), cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.6));

        MetadataScan scan;
        try
        {
            scan = FileMetadataScanner.Scan(data);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A damaged container must never fail the whole item; the catalog keeps the file without tags.
            scan = new MetadataScan();
        }

        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(BuildValues(scan, current));
    }

    internal static IReadOnlyList<MediaMetadataValue> BuildValues(MetadataScan scan, FileMetadataSettings settings)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(settings);

        var values = new List<MediaMetadataValue>();
        if (settings.KeywordsEnabled)
        {
            AddTagValue(values, FileMetadataSchema.Keywords, scan.Keywords);
            AddTagValue(values, FileMetadataSchema.HierarchicalKeywords,
                ExpandHierarchical(scan.HierarchicalPaths, settings.SplitHierarchicalTags));
        }

        if (settings.CommentsEnabled && !string.IsNullOrWhiteSpace(scan.Comment))
        {
            values.Add(new MediaMetadataValue(FileMetadataSchema.Comment, MediaMetadataType.Text,
                Text: scan.Comment));
        }

        if (settings.RatingsEnabled && scan.Rating is { } rating)
        {
            values.Add(new MediaMetadataValue(FileMetadataSchema.Rating, MediaMetadataType.Integer,
                Integer: rating));
        }

        return values;
    }

    /// <summary>
    /// Ready-to-publish hierarchical tags. Splitting emits cumulative prefixes, so filtering on an upper level
    /// still finds the item while the leaf keeps its full context.
    /// </summary>
    internal static IReadOnlyList<string> ExpandHierarchical(IReadOnlyList<string> paths, bool split)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!split)
        {
            return paths;
        }

        var tags = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var prefix = string.Empty;
            foreach (var level in path.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                prefix = prefix.Length == 0 ? level : prefix + " / " + level;
                if (prefix.Length > FileMetadataSchema.MaxTagLength)
                {
                    break;
                }

                if (tags.Count == FileMetadataSchema.MaxTagCount)
                {
                    return tags;
                }

                if (seen.Add(prefix))
                {
                    tags.Add(prefix);
                }
            }
        }

        return tags;
    }

    private static void AddTagValue(List<MediaMetadataValue> values, string key, IReadOnlyList<string> tags)
    {
        if (tags.Count > 0)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Text,
                Text: string.Join("; ", tags)));
        }
    }

    /// <summary>Reads at most <paramref name="length"/> bytes in bounded chunks, honoring cancellation.</summary>
    private static async Task<byte[]> ReadPrefixAsync(IMediaSource source, int length,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = Math.Min(ChunkBytes, length - offset);
            var chunk = await source.ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
            chunk.Span.CopyTo(buffer.AsSpan(offset));
            offset += chunk.Length;
            if (chunk.Length != count)
            {
                // A short read means the source is smaller than advertised; scan what was actually read.
                Array.Resize(ref buffer, offset);
                break;
            }
        }

        return buffer;
    }
}
