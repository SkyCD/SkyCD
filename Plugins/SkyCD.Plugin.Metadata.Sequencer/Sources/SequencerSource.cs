using System.Xml;
using Melanchall.DryWetMidi.Common;
using SkyCD.Plugin.Metadata.Sequencer.Exceptions;
using SkyCD.Plugin.Metadata.Sequencer.Export;
using SkyCD.Plugin.Metadata.Sequencer.Models;
using SkyCD.Plugin.Metadata.Sequencer.Parsing;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Sequencer.Sources;

internal static class SequencerSource
{
    internal static async Task<MediaExtractionResult> ExtractAsync(MediaFamily family, MediaItem item, IMediaSource source,
        IProgress<MediaProgress>? progress, CancellationToken token)
    {
        var facts = await ReadAsync(family, item, source, false, token).ConfigureAwait(false);
        progress?.Report(new MediaProgress(1));
        return new(facts?.Values.ToArray() ?? []);
    }

    internal static async Task<MediaPreviewResult> PreviewAsync(MediaFamily family, MediaItem item, IMediaSource source,
        IProgress<MediaProgress>? progress, CancellationToken token)
    {
        var facts = await ReadAsync(family, item, source, true, token).ConfigureAwait(false);
        var png = facts is null ? null : SequencerRenderer.Render(facts, family == MediaFamily.Midi, token);
        progress?.Report(new MediaProgress(1));
        return new(png is { Length: > 0 and <= 4 * 1024 * 1024 } ? [new MediaPreviewArtifact("image/png", png)] : []);
    }

    private static async Task<SequencerFacts?> ReadAsync(MediaFamily family, MediaItem item, IMediaSource source, bool preview,
        CancellationToken token)
    {
        var length = source.Length;
        if (length is <= 0 or > 16 * 1024 * 1024)
            return null;

        var data = await source.ReadAsync(0, (int)length, token).ConfigureAwait(false);
        if (data.Length != length)
            return null;
        token.ThrowIfCancellationRequested();
        var extension = item.Extension.ToLowerInvariant();
        var bytes = data.ToArray();
        try
        {
            return family switch
            {
                MediaFamily.Project => ProjectReader.Read(bytes, extension, token),
                MediaFamily.Midi => MidiReader.Read(bytes, extension, preview, token),
                MediaFamily.Module => ModuleReader.Read(bytes, extension, token),
                _ => null
            };
        }
        catch (Exception error) when (error is XmlException or InvalidDataException or EndOfStreamException
            or SequencerDocumentException or MidiException or OverflowException
            || family == MediaFamily.Midi && error is ArgumentException)
        {
            return null;
        }
    }
}
