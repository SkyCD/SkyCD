using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Fonts.Exceptions;
using SkyCD.Plugin.Metadata.Fonts.Models;
using SkyCD.Plugin.Metadata.Fonts.Parsing;

namespace SkyCD.Plugin.Metadata.Fonts;

/// <summary>
/// Publishes the family, style, version and copyright of a font, or of the first face of a ttc/otc
/// collection together with the face count.
/// </summary>
/// <remarks>
/// Only values the name table actually carries are published, so "unknown" stays distinguishable from an
/// empty string. Disabling collection, an unreadable face, a missing name table and an oversized table all
/// yield the same "no values" result instead of a failure, so a scan never stops on one damaged font.
/// </remarks>
public sealed class FontMetadataPlugin : IMediaMetadataCapability, IPluginSettingsConsumer
{
    private const int ProviderPriority = 20;

    private readonly FontPluginOptions options;
    private volatile FontSettings settings = FontSettings.Default;

    public FontMetadataPlugin()
        : this(FontPluginOptions.Default)
    {
    }

    public FontMetadataPlugin(FontPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor
    {
        get
        {
            var current = settings;
            return current.CollectionEnabled
                ? new MediaDescriptor(current.Extensions, FontSchema.ContentTypes, FontSchema.Metadata,
                    Priority: ProviderPriority)
                : new MediaDescriptor([], [], FontSchema.Metadata, Priority: ProviderPriority);
        }
    }

    public void ApplySettings(IReadOnlyDictionary<string, string?> settings) =>
        this.settings = FontSettings.From(settings);

    public Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var current = settings;
        if (!current.CollectionEnabled)
        {
            progress?.Report(new MediaProgress(1));
            return Task.FromResult(new MediaExtractionResult([]));
        }

        IReadOnlyList<FontFace> faces;
        try
        {
            using var stream = new SeekableMediaSourceStream(source, cancellationToken);
            faces = FontFile.ReadFaces(stream, options, cancellationToken);
        }
        catch (FontFormatException)
        {
            progress?.Report(new MediaProgress(1));
            return Task.FromResult(new MediaExtractionResult([]));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            progress?.Report(new MediaProgress(1));
            return Task.FromResult(new MediaExtractionResult([]));
        }

        progress?.Report(new MediaProgress(0.8));
        var values = BuildValues(faces[0], faces.Count);
        progress?.Report(new MediaProgress(1));
        return Task.FromResult(new MediaExtractionResult(values));
    }

    internal static List<MediaMetadataValue> BuildValues(FontFace face, int faceCount)
    {
        ArgumentNullException.ThrowIfNull(face);

        var values = new List<MediaMetadataValue>();
        AddText(values, FontSchema.Family, face.Family);
        AddText(values, FontSchema.Subfamily, face.Subfamily);
        AddText(values, FontSchema.FullName, face.FullName);
        AddText(values, FontSchema.PostScriptName, face.PostScriptName);
        AddText(values, FontSchema.Version, face.Version);
        AddText(values, FontSchema.Copyright, face.Copyright);
        if (faceCount > 1)
        {
            values.Add(new MediaMetadataValue(FontSchema.FaceCount, MediaMetadataType.Integer, Integer: faceCount));
        }

        return values;
    }

    private static void AddText(List<MediaMetadataValue> values, string key, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: text));
        }
    }
}
