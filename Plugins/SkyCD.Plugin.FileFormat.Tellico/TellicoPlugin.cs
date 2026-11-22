using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Tellico.Import;
using SkyCD.Plugin.FileFormat.Tellico.Parsing;

namespace SkyCD.Plugin.FileFormat.Tellico;

/// <summary>
/// File format capability plugin for importing Tellico collections:
/// native <c>.tc</c> archives and plain Tellico XML exports (read-only).
/// Collection items import as <c>entry</c> rows, never as files.
/// </summary>
public sealed class TellicoPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "tellico",
            DisplayName: "Tellico Collection (*.tc)",
            Extensions: [".tc", ".xml"],
            MimeTypes: ["text/xml", "application/xml", "application/x-tellico", "application/zip"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify if it represents a Tellico collection.
    /// Preserves the stream position.
    /// </summary>
    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead)
        {
            return false;
        }

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            return TellicoRecognizer.IsTellico(stream, fileName);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (initialPosition >= 0 && stream.CanSeek)
            {
                stream.Position = initialPosition;
            }
        }
    }

    public async Task<FileFormatReadResult> ReadAsync(
        FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var initialPosition = request.Source.CanSeek ? request.Source.Position : -1;
            try
            {
                if (request.Source.CanSeek &&
                    !TellicoRecognizer.IsTellico(request.Source, request.FileName))
                {
                    throw new Exceptions.TellicoUnsupportedSchemaException("not a Tellico collection");
                }
            }
            finally
            {
                if (initialPosition >= 0 && request.Source.CanSeek)
                {
                    request.Source.Position = initialPosition;
                }
            }

            request.Progress?.Report(10);

            var parseResult = await Task.Run(
                () => TellicoParser.Parse(request.Source, cancellationToken), cancellationToken);
            request.Progress?.Report(50);

            var document = TellicoHierarchyBuilder.Compose(parseResult);
            request.Progress?.Report(100);

            return new FileFormatReadResult
            {
                Success = true,
                Payload = document
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    public Task<FileFormatWriteResult> WriteAsync(
        FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "Tellico collection export is not supported. Format is read-only."
        });
    }
}
