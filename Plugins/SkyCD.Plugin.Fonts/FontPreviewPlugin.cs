using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Fonts.Exceptions;

namespace SkyCD.Plugin.Fonts;

/// <summary>
/// Generates the optional font preview as an index-time image, so the catalog can present a sample of the
/// font's own glyphs next to its metadata.
/// </summary>
/// <remarks>
/// A disabled option, a font above the size bound, an unreadable glyph set and a reached per-folder limit all
/// yield an empty artifact list, the explicit "no preview" state. Metadata extraction is never affected by a
/// preview failure.
/// </remarks>
public sealed class FontPreviewPlugin : IMediaPreviewCapability, IPluginSettingsConsumer
{
    private const int ProviderPriority = 20;
    private const int ReadBufferBytes = 64 * 1024;

    private readonly FontPluginOptions options;
    private readonly PreviewLimitTracker limits = new();
    private readonly Func<byte[], SkiaSharp.SKTypeface?>? createTypeface;
    private volatile FontSettings settings = FontSettings.Default;

    public FontPreviewPlugin()
        : this(FontPluginOptions.Default)
    {
    }

    public FontPreviewPlugin(FontPluginOptions options)
        : this(options, null)
    {
    }

    internal FontPreviewPlugin(FontPluginOptions options, Func<byte[], SkiaSharp.SKTypeface?>? createTypeface)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.createTypeface = createTypeface;
    }

    public MediaDescriptor Descriptor
    {
        get
        {
            var current = settings;
            return current.CollectionEnabled && current.PreviewsEnabled
                ? new MediaDescriptor(current.Extensions, FontSchema.ContentTypes, FontSchema.Preview,
                    Priority: ProviderPriority, IndexTimePreview: true)
                : new MediaDescriptor([], [], FontSchema.Preview, Priority: ProviderPriority,
                    IndexTimePreview: true);
        }
    }

    public void ApplySettings(IReadOnlyDictionary<string, string?> settings) =>
        this.settings = FontSettings.From(settings);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var current = settings;
        if (!current.CollectionEnabled || !current.PreviewsEnabled)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        var signature = string.Join('|', current.PreviewSize, current.PreviewLimitPerFolder, current.PreviewText,
            current.PreviewsEnabled);
        if (!limits.TryReserve(item.GroupKey, current.PreviewLimitPerFolder, signature))
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        try
        {
            if (source.Length <= 0 || source.Length > options.MaxFontBytes)
            {
                limits.Release(item.GroupKey);
                progress?.Report(new MediaProgress(1));
                return new MediaPreviewResult([]);
            }

            progress?.Report(new MediaProgress(0.3));
            var data = await ReadAllAsync(source, cancellationToken).ConfigureAwait(false);
            progress?.Report(new MediaProgress(0.6));

            var image = FontPreviewRenderer.Render(data, current.PreviewText, current.PreviewSize, options,
                cancellationToken, createTypeface);
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([new MediaPreviewArtifact(FontPreviewRenderer.ContentType, image)]);
        }
        catch (OperationCanceledException)
        {
            limits.Release(item.GroupKey);
            throw;
        }
        catch (FontPreviewException)
        {
            limits.Release(item.GroupKey);
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            limits.Release(item.GroupKey);
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }
    }

    private static async Task<byte[]> ReadAllAsync(IMediaSource source, CancellationToken cancellationToken)
    {
        var length = checked((int)source.Length);
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = Math.Min(ReadBufferBytes, length - offset);
            var memory = await source.ReadAsync(offset, chunk, cancellationToken).ConfigureAwait(false);
            if (memory.Length == 0)
            {
                throw new FontPreviewException("the source ended unexpectedly");
            }

            memory.Span[..Math.Min(memory.Length, length - offset)].CopyTo(buffer.AsSpan(offset));
            offset += Math.Min(memory.Length, length - offset);
        }

        return buffer;
    }
}
