using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.GCstar.Import;
using SkyCD.Plugin.FileFormat.GCstar.Parsing;

namespace SkyCD.Plugin.FileFormat.GCstar;

/// <summary>
/// File format capability plugin for importing GCstar collection exports (read-only).
/// </summary>
public sealed class GCstarPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "gcstar",
            DisplayName: "GCstar Collection (*.gcs)",
            Extensions: [".gcs", ".xml"],
            MimeTypes: ["text/xml", "application/xml", "application/x-gcstar"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify if it represents a GCstar collection export.
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
            if (stream.CanSeek)
            {
                return GCstarRecognizer.IsGCstarCollection(stream);
            }

            return false;
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

    public Task<FileFormatReadResult> ReadAsync(
        FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var initialPosition = request.Source.CanSeek ? request.Source.Position : -1;
            try
            {
                if (request.Source.CanSeek &&
                    !GCstarRecognizer.IsGCstarCollection(request.Source))
                {
                    throw new Exceptions.GCstarUnsupportedSchemaException("not a GCstar collection export");
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

            var parseResult = GCstarParser.Parse(request.Source, cancellationToken);
            request.Progress?.Report(50);

            var document = GCstarHierarchyBuilder.Compose(parseResult);
            request.Progress?.Report(100);

            return Task.FromResult(new FileFormatReadResult
            {
                Success = true,
                Payload = document
            });
        }
        catch (Exception exception)
        {
            return Task.FromResult(new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            });
        }
    }

    public Task<FileFormatWriteResult> WriteAsync(
        FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "GCstar collection export is not supported. Format is read-only."
        });
    }
}
