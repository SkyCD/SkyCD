using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.BrokenX.Exceptions;
using SkyCD.Plugin.FileFormat.BrokenX.Export;
using SkyCD.Plugin.FileFormat.BrokenX.Import;
using SkyCD.Plugin.FileFormat.BrokenX.Models;
using SkyCD.Plugin.FileFormat.BrokenX.Parsing;

namespace SkyCD.Plugin.FileFormat.BrokenX;

/// <summary>
/// File format capability plugin for Broken X Disk Manager databases (<c>.bxd</c>, version <c>0x40</c>).
/// </summary>
/// <remarks>
/// Reads a database into the shared flat node rows and writes rows back to a byte-identical archive
/// when the catalog is unchanged: base flags, fixed fields and every property (including unknown
/// descriptors and their order) round-trip verbatim. Merge-import and selected-subtree export are
/// exposed through <see cref="BxdMergeService"/> and <see cref="BxdSubtreeSelector"/>.
/// </remarks>
public sealed class BxdPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability, IDataLossAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "brokenx-bxd",
            DisplayName: "Broken X Database",
            Extensions: [".bxd"],
            MimeTypes: ["application/x-brokenx-database"],
            CanRead: true,
            CanWrite: true);

    public bool PreservesCatalogScope => false;

    public FileFormatDataLossAssessment AssessDataLoss(FileFormatDataLossRequest request) =>
        new(FileFormatDataLossAssessmentBuilder.AssessEntries(request,
            FileFormatEntryFieldSupport.Id | FileFormatEntryFieldSupport.ParentId |
            FileFormatEntryFieldSupport.Kind | FileFormatEntryFieldSupport.Properties));

    /// <summary>
    /// Claims a stream only when it starts with the exact 21-byte signature and version byte.
    /// The extension alone never claims a file. Preserves the stream position.
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
            var header = new byte[BxdFormat.SignatureLength + 1];
            if (ReadFull(stream, header) < header.Length)
            {
                return false;
            }

            for (var i = 0; i < BxdFormat.SignatureLength; i++)
            {
                if (header[i] != BxdFormat.Signature[i])
                {
                    return false;
                }
            }

            return header[BxdFormat.SignatureLength] == BxdFormat.SupportedVersion;
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
            var database = await Task.Run(() => BxdReader.Read(request.Source, cancellationToken), cancellationToken);
            request.Progress?.Report(60);

            var fileStem = Path.GetFileNameWithoutExtension(
                request.FileName ?? request.SourcePath ?? "Broken X Database");
            var document = BxdReportBuilder.Build(database, fileStem, CountItems(database));
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
            var database = await Task.Run(() => BxdReportBuilder.ToDatabase(rows), cancellationToken);
            request.Progress?.Report(60);
            await Task.Run(() => BxdWriter.Write(request.Target, database, cancellationToken), cancellationToken);
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
        if (payload is BxdReportDocument document)
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

        throw new BxdCorruptException("write payload must be a Broken X report document or node row list.");
    }

    private static int CountItems(BxdDatabase database)
    {
        var count = 0;
        var stack = new Stack<BxdItem>(database.Roots);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            count++;
            foreach (var child in item.Children)
            {
                stack.Push(child);
            }
        }

        return count;
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
