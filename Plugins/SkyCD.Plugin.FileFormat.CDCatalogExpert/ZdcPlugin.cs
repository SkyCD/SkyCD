using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.CDCatalogExpert.Exceptions;
using SkyCD.Plugin.FileFormat.CDCatalogExpert.Export;
using SkyCD.Plugin.FileFormat.CDCatalogExpert.Import;
using SkyCD.Plugin.FileFormat.CDCatalogExpert.Models;
using SkyCD.Plugin.FileFormat.CDCatalogExpert.Parsing;

namespace SkyCD.Plugin.FileFormat.CDCatalogExpert;

/// <summary>
/// File format capability plugin for CD Catalog Expert 9.x catalogs
/// (<c>.zdc</c>, <c>chunkSize = 4096</c>).
/// </summary>
/// <remarks>
/// Reads a container into the shared flat node rows and writes rows back to a
/// byte-identical archive when the catalog is unchanged: header profile
/// values, reserved blobs, member fields, chunk reserved words, and payload
/// bytes round-trip verbatim while chains, counts, checksums, and AACS
/// envelopes are recomputed. Encrypted containers and members are rejected
/// with <see cref="ZdcEncryptionNotSupportedException"/> instead of emitting
/// corrupt data.
/// </remarks>
public sealed class ZdcPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "cd-catalog-expert-zdc",
            DisplayName: "CD Catalog Expert 9.x Catalog",
            Extensions: [".zdc"],
            MimeTypes: ["application/x-cdcatalogexpert-catalog"],
            CanRead: true,
            CanWrite: true);

    /// <summary>
    /// Claims a stream only when the file name carries the <c>.zdc</c>
    /// extension and the content starts with the <c>AAMVHFSS</c> container
    /// signature (plus the profile magic, chunk size, and chunk-aligned
    /// length). Preserves the stream position.
    /// </summary>
    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(fileName) &&
            !string.Equals(Path.GetExtension(fileName), ".zdc", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            var header = new byte[ZdcFormat.HeaderSize];
            if (ReadFull(stream, header) < header.Length)
            {
                return false;
            }

            for (var i = 0; i < ZdcFormat.ContainerMagic.Length; i++)
            {
                if (header[ZdcFormat.ContainerMagicOffset + i] != ZdcFormat.ContainerMagic[i])
                {
                    return false;
                }
            }

            for (var i = 0; i < ZdcFormat.ProfileMagic.Length; i++)
            {
                if (header[ZdcFormat.ProfileMagicOffset + i] != ZdcFormat.ProfileMagic[i])
                {
                    return false;
                }
            }

            var chunkSize = (uint)(header[ZdcFormat.ChunkSizeOffset]
                | (header[ZdcFormat.ChunkSizeOffset + 1] << 8)
                | (header[ZdcFormat.ChunkSizeOffset + 2] << 16)
                | (header[ZdcFormat.ChunkSizeOffset + 3] << 24));
            if (chunkSize != ZdcFormat.ChunkSize)
            {
                return false;
            }

            if (stream.CanSeek)
            {
                if (stream.Length < ZdcFormat.HeaderSize ||
                    ((stream.Length - ZdcFormat.ChunkBaseOffset) % ZdcFormat.ChunkSize) != 0)
                {
                    return false;
                }
            }

            return true;
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
            request.Progress?.Report(10);
            var database = await Task.Run(() => ZdcReader.Read(request.Source, cancellationToken), cancellationToken);
            request.Progress?.Report(50);

            var parsed = await Task.Run(
                () => ZdcCatalogParser.Read(database, cancellationToken),
                cancellationToken);
            request.Progress?.Report(80);

            var fileStem = Path.GetFileNameWithoutExtension(
                request.FileName ?? request.SourcePath ?? "CD Catalog Expert");
            var document = ZdcCatalogMapper.BuildRows(parsed, database, fileStem);
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

    public async Task<FileFormatWriteResult> WriteAsync(
        FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = ResolveRows(request.Payload);
            request.Progress?.Report(20);
            var fileStem = Path.GetFileNameWithoutExtension(request.FileName ?? string.Empty);
            var database = await Task.Run(
                () => ZdcCatalogMapper.ToDatabase(rows, fileStem),
                cancellationToken);
            request.Progress?.Report(60);
            await Task.Run(() => ZdcWriter.Write(request.Target, database, cancellationToken), cancellationToken);
            request.Progress?.Report(100);

            return new FileFormatWriteResult { Success = true };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new FileFormatWriteResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    private static IReadOnlyList<Dictionary<string, object?>> ResolveRows(object? payload)
    {
        if (payload is ZdcReportDocument document)
        {
            return document.Rows;
        }

        if (payload is IReadOnlyList<Dictionary<string, object?>> rows)
        {
            return rows;
        }

        if (payload is List<Dictionary<string, object?>> mutable)
        {
            return mutable;
        }

        throw new ZdcCorruptException("write payload must be a CD Catalog Expert report document or node row list.");
    }

    private static int ReadFull(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
