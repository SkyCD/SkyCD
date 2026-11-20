using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.MediaMetadata;

namespace SkyCD.Plugin.FileMetadata;

/// <summary>
/// Publishes the tags, comment and rating a user or a colleague assigned through the operating system: the
/// Explorer Details tab on Windows, XDG extended attributes on Linux and Finder tags on macOS. These values
/// are not stored in the file bytes, so they are read by path while the source is available locally.
/// </summary>
public sealed class OsMetadataPlugin : ILocalMediaMetadataCapability
{
    private const int ProviderPriority = 5;

    public MediaDescriptor Descriptor => new(FileMetadataSchema.AnyExtensions, [],
        FileMetadataSchema.Os, Priority: ProviderPriority);

    public Task<MediaExtractionResult> ExtractAsync(
        LocalMediaContext source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        MetadataScan scan;
        try
        {
            scan = OsMetadataReader.Read(source.Path);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A file without readable shell metadata still belongs in the catalog.
            scan = new MetadataScan();
        }

        progress?.Report(new MediaProgress(1));
        return Task.FromResult(new MediaExtractionResult(MetadataValues.Build(scan, FileMetadataSchema.Tags,
            FileMetadataSchema.Comment, FileMetadataSchema.Rating)));
    }
}
