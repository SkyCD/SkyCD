using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.WinCatalog.Xml.Exceptions;

namespace SkyCD.Plugin.WinCatalog.Xml;

/// <summary>
/// File format capability plugin for importing WinCatalog XML reports (read-only).
/// </summary>
public sealed class WinCatalogXmlPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "wincatalog-xml",
            DisplayName: "WinCatalog XML Report",
            Extensions: [".xml"],
            MimeTypes: ["text/xml", "application/xml", "application/vnd.wincatalog.xml"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify if it represents a WinCatalog XML report.
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
            return WinCatalogXmlRecognizer.IsWinCatalogXml(stream);
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
                    !WinCatalogXmlRecognizer.IsWinCatalogXml(request.Source))
                {
                    throw new WinCatalogXmlRootException();
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

            var parseResult = WinCatalogXmlParser.Parse(request.Source, cancellationToken);
            request.Progress?.Report(50);

            var document = WinCatalogXmlHierarchyBuilder.Compose(parseResult);
            request.Progress?.Report(100);

            return new FileFormatReadResult
            {
                Success = true,
                Payload = document
            };
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
            Error = "WinCatalog XML export is not supported. Format is read-only."
        });
    }
}
