using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Archives;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;

namespace SkyCD.Plugin.Host.Archives;

/// <summary>
/// Extracts an archive into a temporary directory, runs enabled metadata plugins over the extracted
/// entries, and always removes the temporary directory before returning. Cleanup also runs on failure
/// and cancellation.
/// </summary>
public sealed class ArchiveMetadataIndexer(MediaExtensionManager media)
{
    private const int FileBufferBytes = 128 * 1024;

    private readonly MediaExtensionManager media = media ?? throw new ArgumentNullException(nameof(media));

    /// <summary>Gets whether any enabled plugin can collect metadata.</summary>
    public bool HasMetadataProviders => media.HasMetadataProviders;

    /// <summary>
    /// Extracts the archive and indexes metadata for supported file entries. The temporary directory is
    /// deleted before the task completes, including when a plugin fails or the caller cancels.
    /// </summary>
    public async Task<ArchiveIndexResult> IndexAsync(
        IArchivePluginCapability archive,
        Stream source,
        string? fileName = null,
        string? targetDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(source);

        var extraction = await archive.ExtractToTemporaryDirectoryAsync(new ArchiveExtractionRequest
        {
            Source = source,
            FileName = fileName,
            TargetDirectory = targetDirectory
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            if (!media.HasMetadataProviders || !extraction.Inspection.Success)
            {
                return new ArchiveIndexResult(extraction.Inspection, []);
            }

            var metadata = new List<ArchiveEntryMetadata>();
            foreach (var entry in extraction.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Kind != ArchiveEntryKind.File
                    || entry.SkipReason != ArchiveEntrySkipReason.None
                    || string.IsNullOrEmpty(entry.FullPath))
                {
                    continue;
                }

                var item = new MediaItem(Path.GetExtension(entry.FullPath), null, entry.SizeBytes);
                var results = await media.ProcessAsync(
                    item,
                    token => OpenReadAsync(entry.FullPath, token),
                    cancellationToken: cancellationToken,
                    selection: MediaProviderSelection.StandardMetadata).ConfigureAwait(false);
                if (results.Count > 0)
                {
                    metadata.Add(new ArchiveEntryMetadata(entry.Path, results));
                }
            }

            return new ArchiveIndexResult(extraction.Inspection, metadata);
        }
        finally
        {
            await extraction.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            FileBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }
}
