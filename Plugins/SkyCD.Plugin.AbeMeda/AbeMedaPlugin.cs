using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.AbeMeda.Exceptions;

namespace SkyCD.Plugin.AbeMeda;

/// <summary>
/// File format capability plugin for importing abeMeda/NeoFinder-family XML catalog
/// exports (read-only). Native-catalog fidelity is not promised: thumbnail and preview
/// payloads are treated as unavailable and retained as presence markers only.
/// </summary>
public sealed class AbeMedaPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "abemeda-xml",
            DisplayName: "abeMeda XML Export (*.xml)",
            Extensions: [".xml"],
            MimeTypes: ["text/xml", "application/xml", "application/vnd.abemeda.xml"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify it is an abeMeda-family XML export.
    /// Preserves the stream position.
    /// </summary>
    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            return false;
        }

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            return AbeMedaRecognizer.IsAbeMedaXml(stream);
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
                if (request.Source.CanSeek && !AbeMedaRecognizer.IsAbeMedaXml(request.Source))
                {
                    throw new AbeMedaUnsupportedSchemaException();
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
                () => AbeMedaParser.Parse(request.Source, cancellationToken), cancellationToken);
            request.Progress?.Report(50);

            var document = AbeMedaHierarchyBuilder.Compose(parseResult);
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
            Error = "abeMeda XML export is not supported. Format is read-only."
        });
    }
}
