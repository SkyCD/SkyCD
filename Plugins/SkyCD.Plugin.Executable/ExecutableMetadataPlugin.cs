using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Executable.Exceptions;
using SkyCD.Plugin.Executable.Models;
using SkyCD.Plugin.Executable.Parsing;

namespace SkyCD.Plugin.Executable;

/// <summary>
/// Publishes the version information and technical facts of a Windows executable.
/// </summary>
/// <remarks>
/// The plugin holds bytes, never a path, and reads only the header, the section table, the resource directory
/// nodes and the version resource, so a scan never loads a whole program. A source that is not an executable,
/// is damaged, or carries no version information still produces values — the container and architecture are
/// facts of the file itself. Values the image does not carry are omitted, so "unknown" stays distinguishable
/// from "none"; the shared schema with the icon capability is what lets <c>Container</c> be published once.
/// </remarks>
public sealed class ExecutableMetadataPlugin : IMediaMetadataCapability
{
    private const int ProviderPriority = 20;

    private readonly ExecutablePluginOptions options;

    public ExecutableMetadataPlugin()
        : this(ExecutablePluginOptions.Default)
    {
    }

    public ExecutableMetadataPlugin(ExecutablePluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(ExecutableSchema.Extensions, ExecutableSchema.ContentTypes, ExecutableSchema.Metadata,
            Priority: ProviderPriority);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);

        cancellationToken.ThrowIfCancellationRequested();
        var length = source.Length;
        if (length <= 0 || length > options.MaxSourceBytes)
        {
            // Empty or unreadable content is not an executable at all; report no values rather than a failure.
            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult([]);
        }

        var imageSource = new ExecutableSource(source, options.MaxSourceBytes);
        ExecutableImage image;
        try
        {
            image = await ExecutableImage.ReadAsync(imageSource, cancellationToken).ConfigureAwait(false);
        }
        catch (ExecutableReadException)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult([]);
        }

        progress?.Report(new MediaProgress(0.4));

        var values = new List<MediaMetadataValue>
        {
            new(ExecutableSchema.Container, MediaMetadataType.Text, Text: image.Container)
        };

        AddText(values, ExecutableSchema.Architecture, image.MachineName);
        if (image.IsPortableExecutable)
        {
            values.Add(new MediaMetadataValue(ExecutableSchema.IsExecutableImage, MediaMetadataType.Boolean,
                Boolean: image.IsExecutableImage));
            values.Add(new MediaMetadataValue(ExecutableSchema.IsDotNet, MediaMetadataType.Boolean,
                Boolean: image.IsDotNet));
            AddText(values, ExecutableSchema.PeFormat, image.PeFormat);
        }

        if (image.IsPortableExecutable && image.HasResources)
        {
            ExecutableResources resources;
            try
            {
                resources = await ExecutableResources.ReadAsync(image, imageSource, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ExecutableReadException)
            {
                // A damaged resource directory costs the resource values, never the header facts.
                resources = ExecutableResources.Empty;
            }

            progress?.Report(new MediaProgress(0.7));
            await AddVersionValuesAsync(values, imageSource, resources, cancellationToken).ConfigureAwait(false);
            await AddIconFactsAsync(values, imageSource, resources, cancellationToken).ConfigureAwait(false);
        }

        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(values);
    }

    private async Task AddVersionValuesAsync(List<MediaMetadataValue> values, ExecutableSource source,
        ExecutableResources resources, CancellationToken cancellationToken)
    {
        if (resources.VersionInfo is not { } version || version.Length > ExecutableResources.MaximumResourceBytes)
        {
            return;
        }

        byte[] data;
        try
        {
            data = await source.ReadAsync(version.DataOffset, version.Length, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ExecutableReadException)
        {
            return;
        }

        var strings = VersionInfoReader.Read(data);
        foreach (var (key, value) in strings)
        {
            var field = key switch
            {
                "FileVersion" => ExecutableSchema.FileVersion,
                "ProductVersion" => ExecutableSchema.ProductVersion,
                "CompanyName" => ExecutableSchema.CompanyName,
                "ProductName" => ExecutableSchema.ProductName,
                "FileDescription" => ExecutableSchema.FileDescription,
                "OriginalFilename" => ExecutableSchema.OriginalFilename,
                "InternalName" => ExecutableSchema.InternalName,
                "LegalCopyright" => ExecutableSchema.LegalCopyright,
                _ => null
            };

            if (field is not null)
            {
                AddText(values, field, value);
            }
        }
    }

    private async Task AddIconFactsAsync(List<MediaMetadataValue> values, ExecutableSource source,
        ExecutableResources resources, CancellationToken cancellationToken)
    {
        if (resources.IconGroups.Count == 0)
        {
            return;
        }

        var facts = await ExecutableIconExtractor
            .ExtractAsync(source, resources, options, cancellationToken)
            .ConfigureAwait(false);
        if (facts.ImageCount == 0)
        {
            return;
        }

        values.Add(new MediaMetadataValue(ExecutableSchema.IconImageCount, MediaMetadataType.Integer,
            Integer: facts.ImageCount));
        values.Add(new MediaMetadataValue(ExecutableSchema.LargestIconWidth, MediaMetadataType.Integer,
            Integer: facts.LargestWidth));
        values.Add(new MediaMetadataValue(ExecutableSchema.LargestIconHeight, MediaMetadataType.Integer,
            Integer: facts.LargestHeight));
    }

    private void AddText(List<MediaMetadataValue> values, string key, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var value = text.Trim();
        if (value.Length > options.MaxMetadataValueCharacters)
        {
            value = value[..options.MaxMetadataValueCharacters];
        }

        values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: value));
    }
}
