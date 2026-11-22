using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Librer.Parsing;

namespace SkyCD.Plugin.FileFormat.Librer;

/// <summary>
/// File format capability plugin for importing Librer disk database files (*.librer_data, *.librer_data_compr, *.dat).
/// </summary>
public sealed class LibrerPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    private static readonly byte[] ZipLocalHeader = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] ZipEmptyHeader = [0x50, 0x4B, 0x05, 0x06];
    private static readonly byte[] ZipSpannedHeader = [0x50, 0x4B, 0x07, 0x08];

    private readonly LibrerParser _parser = new();

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "librer",
            DisplayName: "Librer Catalog (*.librer_data, *.librer_data_compr, *.dat)",
            Extensions: [".librer_data", ".librer_data_compr", ".dat"],
            MimeTypes: ["application/x-librer", "application/zip", "application/octet-stream"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify if it represents a Librer catalog archive.
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
            var header = new byte[4];
            var bytesRead = stream.Read(header, 0, header.Length);
            if (bytesRead < 4)
            {
                return false;
            }

            var isZip = IsZipHeader(header);
            if (!isZip)
            {
                return false;
            }

            if (stream.CanSeek)
            {
                stream.Position = 0;
                try
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                    var hasHeader = archive.GetEntry("HEADER") is not null || archive.GetEntry("header") is not null;
                    var hasFilestructure = archive.GetEntry("filestructure") is not null;
                    var hasFilenames = archive.GetEntry("filenames") is not null;

                    return hasHeader && hasFilestructure && hasFilenames;
                }
                catch
                {
                    return false;
                }
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

    private static bool IsZipHeader(byte[] header)
    {
        return Matches(header, ZipLocalHeader) || Matches(header, ZipEmptyHeader) || Matches(header, ZipSpannedHeader);
    }

    private static bool Matches(byte[] buffer, byte[] pattern)
    {
        if (buffer.Length < pattern.Length) return false;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (buffer[i] != pattern[i]) return false;
        }
        return true;
    }

    public Task<FileFormatReadResult> ReadAsync(
        FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var document = _parser.Parse(
                request.Source,
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
            Error = "Librer export is not supported. Format is read-only."
        });
    }
}
