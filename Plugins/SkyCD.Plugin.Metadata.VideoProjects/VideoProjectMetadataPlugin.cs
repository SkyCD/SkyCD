using System.Xml;
using ICSharpCode.SharpZipLib;
using SkyCD.Plugin.Metadata.VideoProjects.Models;
using SkyCD.Plugin.Metadata.VideoProjects.Parsing;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.VideoProjects;

public sealed class VideoProjectMetadataPlugin : IMediaMetadataCapability
{
    public MediaDescriptor Descriptor { get; } =
        new([".prproj", ".aep", ".aepx", ".drp", ".drt", ".veg"], [], VideoProjectSchema.Metadata, Priority: 10);

    public Task<MediaExtractionResult> ExtractAsync(
        MediaItem item, IMediaSource source, IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
    {
        // The host dispatches the capability on a worker; ZIP and XML readers use synchronous streams.
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new ProjectSourceStream(source, cancellationToken);
        ProjectFacts? facts;
        try
        {
            facts = item.Extension.ToLowerInvariant() switch
            {
                ".prproj" => PremiereProjectReader.Read(stream, cancellationToken),
                ".aep" => AfterEffectsProjectReader.Read(stream, cancellationToken),
                ".aepx" => AfterEffectsXmlProjectReader.Read(stream, cancellationToken),
                ".drp" => ResolveProjectReader.Read(stream, cancellationToken),
                ".drt" => ResolveTemplateReader.Read(stream, cancellationToken),
                ".veg" => VegasProjectReader.Read(stream),
                _ => null
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or XmlException or SharpZipBaseException)
        {
            facts = null;
        }
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(1));
        return Task.FromResult(facts?.ToResult() ?? new MediaExtractionResult([]));
    }
}
