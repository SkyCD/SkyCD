using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Models;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Parsing;

namespace SkyCD.Plugin.FileFormat.LegacySkyCD;

public sealed class LegacyCscdPlugin : IFileFormatPluginCapability, IDataLossAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "legacy-cscd",
            DisplayName: "SkyCD Compressed Text Format",
            Extensions: [".cscd"],
            MimeTypes: ["application/vnd.skycd.cscd"],
            CanRead: true,
            CanWrite: true);

    public bool PreservesCatalogScope => false;

    public FileFormatDataLossAssessment AssessDataLoss(FileFormatDataLossRequest request) =>
        new(FileFormatDataLossAssessmentBuilder.AssessEntries(request,
            FileFormatEntryFieldSupport.Kind | FileFormatEntryFieldSupport.Size));

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var compressed = new DeflateStream(request.Source, CompressionMode.Decompress, leaveOpen: true);
            using var reader = new StreamReader(compressed, Encoding.UTF8, leaveOpen: true);
            var catalog = new LegacyCscdCatalog();
            string? line;
            var processed = 0;

            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                processed++;
                if (LegacyCatalogLine.IsIgnored(line))
                {
                    continue;
                }

                var parsed = LegacyCatalogLine.Parse(line.Trim());
                catalog.Entries.Add(new LegacyCscdEntry
                {
                    Path = parsed.Path,
                    SizeBytes = parsed.SizeBytes,
                    IsDirectory = parsed.IsDirectory
                });

                request.Progress?.Report(Math.Min(100, processed % 100));
            }

            request.Progress?.Report(100);
            return new FileFormatReadResult { Success = true, Payload = catalog };
        }
        catch (Exception exception)
        {
            return new FileFormatReadResult { Success = false, Error = exception.Message };
        }
    }

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var catalog = ResolveCatalog(request.Payload);
        if (catalog is null)
        {
            return new FileFormatWriteResult { Success = false, Error = "Payload must be LegacyCscdCatalog." };
        }

        try
        {
            using var compressed = new DeflateStream(request.Target, CompressionMode.Compress, leaveOpen: true);
            using var writer = new StreamWriter(compressed, Encoding.UTF8, leaveOpen: true);
            for (var i = 0; i < catalog.Entries.Count; i++)
            {
                var entry = catalog.Entries[i];
                await writer.WriteLineAsync(LegacyCatalogLine.Format(entry.Path, entry.SizeBytes).AsMemory(),
                    cancellationToken);
                request.Progress?.Report((int)((i + 1d) / catalog.Entries.Count * 100d));
            }

            await writer.FlushAsync(cancellationToken);
            request.Progress?.Report(100);
            return new FileFormatWriteResult { Success = true };
        }
        catch (Exception exception)
        {
            return new FileFormatWriteResult { Success = false, Error = exception.Message };
        }
    }

    private static LegacyCscdCatalog? ResolveCatalog(object? payload)
    {
        if (payload is LegacyCscdCatalog typed)
        {
            return typed;
        }

        if (payload is not IEnumerable<Dictionary<string, object?>> rows)
        {
            return null;
        }

        var catalog = new LegacyCscdCatalog();
        foreach (var entry in LegacyRowPaths.Build(rows))
        {
            catalog.Entries.Add(new LegacyCscdEntry
            {
                Path = entry.Path,
                SizeBytes = entry.SizeBytes,
                IsDirectory = entry.IsDirectory
            });
        }

        return catalog;
    }
}
