using System.Text;
using System.Text.RegularExpressions;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Legacy.Scd;

public sealed class LegacyScdPlugin : IFileFormatPluginCapability
{
    private static readonly Regex SizePrefix = new(@"^\[(?<size>[^\]]+)\]\s*(?<path>.+)$", RegexOptions.Compiled);

    public FileFormatDescriptor SupportedFormat =>
        new FileFormatDescriptor("legacy-scd", "SkyCD Text Format", [".scd"], CanRead: true, CanWrite: true, "text/plain")
    ;

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
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
                    var size = TryParseLegacySize(sizeMatch.Groups["size"].Value);
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

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
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
                    ? $"[{FormatLegacySize(entry.SizeBytes.Value)}] {entry.Path}"
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

    private static long? TryParseLegacySize(string raw)
    {
        var value = raw.Trim().ToUpperInvariant();
        if (value.EndsWith("KB") && double.TryParse(value[..^2], out var kb)) return (long)(kb * 1024d);
        if (value.EndsWith("MB") && double.TryParse(value[..^2], out var mb)) return (long)(mb * 1024d * 1024d);
        if (value.EndsWith("GB") && double.TryParse(value[..^2], out var gb)) return (long)(gb * 1024d * 1024d * 1024d);
        if (long.TryParse(value, out var bytes)) return bytes;
        return null;
    }

    private static string FormatLegacySize(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024d * 1024d * 1024d):0.##}GB";
        if (bytes >= 1024L * 1024L) return $"{bytes / (1024d * 1024d):0.##}MB";
        if (bytes >= 1024L) return $"{bytes / 1024d:0.##}KB";
        return bytes.ToString();
    }
}
