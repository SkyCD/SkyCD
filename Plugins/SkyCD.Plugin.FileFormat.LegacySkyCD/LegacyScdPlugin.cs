using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Exceptions;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Models;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Parsing;

namespace SkyCD.Plugin.FileFormat.LegacySkyCD;

public sealed class LegacyScdPlugin : IFileFormatPluginCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "legacy-scd",
            DisplayName: "SkyCD Legacy Text Format",
            Extensions: [".scd"],
            MimeTypes: ["application/vnd.skycd.scd"],
            CanRead: true,
            CanWrite: true);

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var buffered = new MemoryStream();
            await request.Source.CopyToAsync(buffered, cancellationToken);
            buffered.Position = 0;
            var signature = new byte[(int)Math.Min(buffered.Length, 12)];
            buffered.ReadExactly(signature);
            buffered.Position = 0;
            if (Encoding.ASCII.GetString(signature).Equals("# SkyCD File", StringComparison.OrdinalIgnoreCase))
            {
                var binaryCatalog = LegacyScdBinaryReader.Read(buffered, cancellationToken);
                request.Progress?.Report(100);
                return new FileFormatReadResult { Success = true, Payload = binaryCatalog };
            }

            using var reader = new StreamReader(buffered, Encoding.UTF8, leaveOpen: true);
            var catalog = new LegacyScdCatalog();
            string? line;
            var processed = 0;

            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                processed++;
                if (LegacyCatalogLine.IsIgnored(line))
                {
                    continue;
                }

                if (line.Contains('\0'))
                    throw new InvalidLegacyScdBinaryException("unrecognized binary header");

                var parsed = LegacyCatalogLine.Parse(line.Trim());
                catalog.Entries.Add(new LegacyScdEntry
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
        if (request.Payload is not LegacyScdCatalog catalog)
        {
            return new FileFormatWriteResult { Success = false, Error = "Payload must be LegacyScdCatalog." };
        }

        try
        {
            using var writer = new StreamWriter(request.Target, Encoding.UTF8, leaveOpen: true);
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
}
