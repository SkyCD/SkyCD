using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Exceptions;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Import;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker;

/// <summary>
/// Read-only file format plugin for native DiskCatalogMaker v5 catalogs
/// (.dcmf single-file catalogs and .dcmd package catalogs).
/// </summary>
public sealed class DiskCatalogMakerV5Plugin :
    IFileFormatPluginCapability,
    IContentAwareFileFormatCapability,
    IFolderAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "diskcatalogmaker-v5",
            DisplayName: "DiskCatalogMaker Catalog v5 (*.dcmf, *.dcmd)",
            Extensions: [".dcmf", ".dcmd"],
            MimeTypes: ["application/x-disccatalogmaker-catalog", "application/x-disccatalogmaker-catalog-package"],
            CanRead: true,
            CanWrite: false,
            SupportsFolder: true);

    /// <summary>
    /// Recognises the v5 binary header by structure, not by file extension.
    /// Preserves the stream position.
    /// </summary>
    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (stream is null || !stream.CanRead || !stream.CanSeek)
        {
            return false;
        }

        var initialPosition = stream.Position;
        try
        {
            Span<byte> header = stackalloc byte[DiskCatalogMakerV5Format.HeaderSize];
            return ReadHeader(stream, header) && LooksLikeCatalogHeader(header);
        }
        catch
        {
            return false;
        }
        finally
        {
            stream.Position = initialPosition;
        }
    }

    /// <summary>
    /// Recognises a .dcmd package by requiring a structurally valid Catalog.dcm payload.
    /// </summary>
    public bool CanHandleFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return false;
        }

        try
        {
            var catalogPath = Path.Combine(folderPath, "Catalog.dcm");
            if (!File.Exists(catalogPath))
            {
                return false;
            }

            using var stream = new FileStream(
                catalogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64, FileOptions.SequentialScan);
            Span<byte> header = stackalloc byte[DiskCatalogMakerV5Format.HeaderSize];
            return ReadHeader(stream, header) && LooksLikeCatalogHeader(header);
        }
        catch
        {
            return false;
        }
    }

    public async Task<FileFormatReadResult> ReadAsync(
        FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sourcePath = ResolveSourcePath(request);
            string? catalogPath = null;
            string? packageRoot = null;

            if (!string.IsNullOrWhiteSpace(sourcePath) && Directory.Exists(sourcePath))
            {
                packageRoot = sourcePath;
                catalogPath = Path.Combine(sourcePath, "Catalog.dcm");
                if (!File.Exists(catalogPath))
                {
                    throw new DiskCatalogMakerV5MissingCatalogException(sourcePath);
                }
            }
            else if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            {
                catalogPath = sourcePath;
                if (string.Equals(Path.GetFileName(sourcePath), "Catalog.dcm", StringComparison.OrdinalIgnoreCase))
                {
                    packageRoot = Path.GetDirectoryName(sourcePath);
                }
            }

            request.Progress?.Report(5);
            cancellationToken.ThrowIfCancellationRequested();

            var payload = catalogPath is not null
                ? await ReadFileBoundedAsync(catalogPath, cancellationToken)
                : await ReadStreamBoundedAsync(request.Source, cancellationToken);

            request.Progress?.Report(40);
            cancellationToken.ThrowIfCancellationRequested();

            var catalog = DiskCatalogMakerV5Parser.Parse(payload, catalogPath ?? request.FileName ?? "catalog");

            request.Progress?.Report(70);
            cancellationToken.ThrowIfCancellationRequested();

            var document = DiskCatalogMakerV5HierarchyBuilder.Build(catalog, packageRoot);

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
            Error = "DiskCatalogMaker native catalogs are not supported for writing. Format is read-only."
        });
    }

    private static string? ResolveSourcePath(FileFormatReadRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SourcePath))
        {
            return request.SourcePath;
        }

        if (!string.IsNullOrWhiteSpace(request.FileName))
        {
            var candidate = request.FileName;
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool ReadHeader(Stream stream, Span<byte> header)
    {
        var total = 0;
        while (total < header.Length)
        {
            var read = stream.Read(header[total..]);
            if (read == 0)
            {
                return false;
            }

            total += read;
        }

        return true;
    }

    private static bool LooksLikeCatalogHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < DiskCatalogMakerV5Format.HeaderSize)
        {
            return false;
        }

        if (!header[..DiskCatalogMakerV5Format.Magic.Length].SequenceEqual(DiskCatalogMakerV5Format.Magic))
        {
            return false;
        }

        var headerSize = (ushort)(header[4] | (header[5] << 8));
        var endianMarker = (ushort)(header[10] | (header[11] << 8));
        return headerSize == DiskCatalogMakerV5Format.HeaderSize &&
               endianMarker == DiskCatalogMakerV5Format.LittleEndianMarker;
    }

    private static async Task<byte[]> ReadFileBoundedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ReadStreamBoundedAsync(stream, cancellationToken);
    }

    private static async Task<byte[]> ReadStreamBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream is null)
        {
            throw new DiskCatalogMakerV5CorruptCatalogException("no catalog source was supplied.");
        }

        if (stream.CanSeek && stream.Length > DiskCatalogMakerV5Format.MaximumCatalogBytes)
        {
            throw new DiskCatalogMakerV5CatalogTooLargeException(
                stream.Length, DiskCatalogMakerV5Format.MaximumCatalogBytes);
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > DiskCatalogMakerV5Format.MaximumCatalogBytes)
            {
                throw new DiskCatalogMakerV5CatalogTooLargeException(
                    total, DiskCatalogMakerV5Format.MaximumCatalogBytes);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }
}
