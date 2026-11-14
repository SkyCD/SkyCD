using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.CdCat.Hcf;

/// <summary>
/// File format capability plugin for importing CdCat .hcf (Hyper Catalog File) catalogs.
/// </summary>
public sealed class CdCatHcfPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "cdcat-hcf",
            DisplayName: "CdCat Catalog (*.hcf)",
            Extensions: [".hcf"],
            MimeTypes: ["application/x-cdcat", "application/x-hcf", "application/gzip"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify if it represents a CdCat catalog (.hcf gzip or XML).
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
            var header = new byte[2];
            var bytesRead = stream.Read(header, 0, 2);
            if (bytesRead < 2)
            {
                return false;
            }

            var isGzip = (header[0] == 0x1F && header[1] == 0x8B);

            if (stream.CanSeek)
            {
                stream.Position = initialPosition;
            }

            Stream? gzipToDispose = null;
            Stream targetStream;
            if (isGzip)
            {
                gzipToDispose = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
                targetStream = gzipToDispose;
            }
            else
            {
                targetStream = stream;
            }

            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 64 * 1024,
                    IgnoreComments = true,
                    IgnoreWhitespace = true,
                    IgnoreProcessingInstructions = true
                };

                using var xmlReader = XmlReader.Create(targetStream, settings);
                while (xmlReader.Read())
                {
                    if (xmlReader.NodeType == XmlNodeType.Element)
                    {
                        if (string.Equals(xmlReader.LocalName, "datafile", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        if (string.Equals(xmlReader.LocalName, "catalog", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        return false;
                    }
                }
            }
            finally
            {
                gzipToDispose?.Dispose();
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
            var document = CdCatHcfParser.Parse(
                request.Source,
                CdCatHcfParser.DefaultMaxDecompressedBytes,
                request.Progress,
                cancellationToken);

            return Task.FromResult(new FileFormatReadResult
            {
                Success = true,
                Payload = document
            });
        }
        catch (OperationCanceledException)
        {
            throw;
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
            Error = "CdCat (.hcf) export is not supported. Format is read-only."
        });
    }
}
