using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Formatting;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Legacy.Scd;

public sealed class LegacyScdPlugin : IFileFormatPluginCapability
{
    private static readonly Regex SizePrefix = new(@"^\[(?<size>[^\]]+)\]\s*(?<path>.+)$", RegexOptions.Compiled);

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "legacy-scd",
            DisplayName: "SkyCD Text Format",
            Extensions: [".scd"],
            MimeTypes: ["application/vnd.skycd.scd"],
            CanRead: true,
            CanWrite: true);

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var reader = new StreamReader(request.Source, Encoding.UTF8, leaveOpen: true);
            var catalog = new LegacyScdCatalog();
            string? line;
            var processed = 0;

            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                processed++;
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var trimmed = line.Trim();
                var sizeMatch = SizePrefix.Match(trimmed);
                if (sizeMatch.Success)
                {
                    var size = SizeFormatting.TryParseBytes(sizeMatch.Groups["size"].Value, out var parsedBytes)
                        ? (long?)parsedBytes
                        : null;
                    catalog.Entries.Add(new LegacyScdEntry
                    {
                        Path = sizeMatch.Groups["path"].Value.Trim(),
                        SizeBytes = size
                    });
                }
                else
                {
                    catalog.Entries.Add(new LegacyScdEntry { Path = trimmed });
                }

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
                var line = entry.SizeBytes is > 0
                    ? $"[{SizeFormatting.FormatBytes(entry.SizeBytes.Value, "0.##", removeSpace: true)}] {entry.Path}"
                    : entry.Path;
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
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
