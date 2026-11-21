using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.OpenOffice.Exceptions;
using SkyCD.Plugin.OpenOffice.Models;
using SkyCD.Plugin.OpenOffice.Parsing;

namespace SkyCD.Plugin.OpenOffice;

/// <summary>
/// Extracts document metadata from OpenDocument and legacy OpenOffice.org packages without altering them.
/// </summary>
/// <remarks>
/// The plugin holds bytes through the host's bounded source, never a path, so nothing in a package can make
/// it open another file. It reads the declared MIME type, the package manifest and <c>meta.xml</c> only; the
/// manifest is used to distinguish an encrypted package from a damaged one, and no credential is ever
/// requested, guessed, stored or used. Fields the package does not carry are omitted rather than published
/// as empty strings, so "unknown" stays distinguishable from "none".
/// </remarks>
public sealed class OpenOfficeMetadataPlugin : IMediaMetadataCapability
{
    private const int MaxContentTypeBytes = 4096;

    private readonly OpenOfficePluginOptions options;

    public OpenOfficeMetadataPlugin()
        : this(OpenOfficePluginOptions.Default)
    {
    }

    public OpenOfficeMetadataPlugin(OpenOfficePluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(OpenOfficeSchema.Extensions, OpenOfficeSchema.ContentTypes, OpenOfficeSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var data = await OpenOfficeSourceReader
                .ReadAllAsync(item, source, options.MaxSourceBytes, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new MediaProgress(0.3));

            using var package = OpenOfficePackage.Open(data);
            cancellationToken.ThrowIfCancellationRequested();

            var contentType = package.TryReadContentType(MaxContentTypeBytes);
            var encrypted = package.DeclaresEncryption(options.MaxMetadataBytes);
            var metadataBytes = package.TryReadMetadata(options.MaxMetadataBytes);
            progress?.Report(new MediaProgress(0.6));

            var metadata = ReadMetadata(metadataBytes, encrypted, cancellationToken);
            progress?.Report(new MediaProgress(0.9));

            var values = BuildValues(item, contentType, encrypted, metadata);
            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult(values);
        }
        catch (Exception exception) when (exception is OpenOfficeDecodeException
                                              or OpenOfficeAuthenticationRequiredException
                                              or OpenOfficeSourceTooLargeException
                                              or OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OpenOfficeDecodeException("the document structure could not be parsed", exception);
        }
    }

    private static OpenOfficeMetadata? ReadMetadata(
        byte[]? metadataBytes,
        bool encrypted,
        CancellationToken cancellationToken)
    {
        if (metadataBytes is null)
        {
            // A readable manifest that declares encryption and no readable metadata is a protected package;
            // an ordinary package without meta.xml still publishes the container facts below.
            if (encrypted)
            {
                throw new OpenOfficeAuthenticationRequiredException("the metadata entry is encrypted");
            }

            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return OpenOfficeMetaReader.Read(metadataBytes, metadataBytes.Length);
        }
        catch (OpenOfficeDecodeException exception) when (encrypted)
        {
            throw new OpenOfficeAuthenticationRequiredException(
                "the encrypted metadata entry could not be read", exception);
        }
    }

    private static List<MediaMetadataValue> BuildValues(
        MediaItem item,
        string? contentType,
        bool encrypted,
        OpenOfficeMetadata? metadata)
    {
        var values = new List<MediaMetadataValue>();

        AddText(values, OpenOfficeSchema.Container,
            OpenOfficeDocumentTypes.ResolveContainer(contentType, item.Extension));
        AddText(values, OpenOfficeSchema.DocumentType,
            OpenOfficeDocumentTypes.ResolveDocumentType(contentType, item.Extension));
        AddText(values, OpenOfficeSchema.FormatVersion, metadata?.FormatVersion);
        values.Add(new MediaMetadataValue(OpenOfficeSchema.IsEncrypted, MediaMetadataType.Boolean,
            Boolean: encrypted));

        if (metadata is not null)
        {
            values.AddRange(metadata.Values);
        }

        return values;
    }

    private static void AddText(List<MediaMetadataValue> values, string key, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: text.Trim()));
        }
    }
}
