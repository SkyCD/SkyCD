using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.WhereIsIt.Exceptions;
using SkyCD.Plugin.FileFormat.WhereIsIt.Import;
using SkyCD.Plugin.FileFormat.WhereIsIt.Parsing;

namespace SkyCD.Plugin.FileFormat.WhereIsIt;

/// <summary>
/// File format capability plugin for importing Where Is It? XML reports (read-only).
/// </summary>
public sealed class WhereIsItPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "whereisit-xml-report",
            DisplayName: "Where Is It? XML Report (*.xml)",
            Extensions: [".xml"],
            MimeTypes: ["text/xml", "application/xml", "application/vnd.whereisit.report+xml"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify it is a Where Is It? XML report.
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
            return WhereIsItRecognizer.IsWhereIsItXml(stream);
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
                if (request.Source.CanSeek && !WhereIsItRecognizer.IsWhereIsItXml(request.Source))
                {
                    throw new WhereIsItUnsupportedSchemaException();
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
                () => WhereIsItParser.Parse(request.Source, cancellationToken), cancellationToken);
            request.Progress?.Report(50);

            var document = WhereIsItHierarchyBuilder.Compose(parseResult);
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
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "Where Is It? XML report import is read-only. Export to another format instead."
        });
    }
}
