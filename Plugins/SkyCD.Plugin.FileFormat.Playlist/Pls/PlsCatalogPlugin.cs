using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Playlist.Export;
using SkyCD.Plugin.FileFormat.Playlist.Pls.Models;

namespace SkyCD.Plugin.FileFormat.Playlist.Pls;

public sealed class PlsCatalogPlugin : IFileFormatPluginCapability, IDataLossAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-pls",
            DisplayName: "PLS Playlist",
            Extensions: [".pls"],
            MimeTypes: ["audio/x-scpls", "audio/scpls"],
            CanRead: true,
            CanWrite: true);

    public bool PreservesCatalogScope => false;

    public FileFormatDataLossAssessment AssessDataLoss(FileFormatDataLossRequest request) =>
        new(FileFormatDataLossAssessmentBuilder.AssessEntries(request, FileFormatEntryFieldSupport.Size));

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var reader = new StreamReader(request.Source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);
            var lines = new List<string>();
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                lines.Add(line);
            }

            if (lines.Count == 0 || !string.Equals(lines[0].Trim(), "[playlist]", StringComparison.OrdinalIgnoreCase))
            {
                return new FileFormatReadResult
                {
                    Success = false,
                    Error = "PLS file must start with [playlist] header."
                };
            }

            var values = ParseKeyValue(lines.Skip(1));
            var entries = new List<PlsPlaylistEntry>();

            foreach (var (key, value) in values)
            {
                if (!key.StartsWith("File", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var suffix = key["File".Length..];
                if (!int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ||
                    string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var title = values.TryGetValue($"Title{index}", out var titleValue) ? titleValue : null;
                var size = 0L;
                if (values.TryGetValue($"Length{index}", out var lengthValue) &&
                    long.TryParse(lengthValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    size = Math.Max(0L, parsed);
                }

                entries.Add(new PlsPlaylistEntry(value.Trim(), title, size));
            }

            return new FileFormatReadResult
            {
                Success = true,
                Payload = new PlsPlaylistDocument(entries.OrderBy(static entry => entry.Path, StringComparer.Ordinal).ToList())
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

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = ResolveEntries(request.Payload);
            await using var writer = PlaylistTextWriter.Create(request.Target);
            await writer.WriteLineAsync("[playlist]");

            var index = 1;
            foreach (var entry in entries)
            {
                await writer.WriteLineAsync(FormattableString.Invariant($"File{index}={entry.Path}"));
                if (!string.IsNullOrWhiteSpace(entry.Title))
                {
                    await writer.WriteLineAsync(FormattableString.Invariant($"Title{index}={entry.Title}"));
                }

                await writer.WriteLineAsync(FormattableString.Invariant($"Length{index}={entry.SizeBytes}"));
                index++;
            }

            await writer.WriteLineAsync(FormattableString.Invariant($"NumberOfEntries={entries.Count}"));
            await writer.WriteLineAsync("Version=2");
            await writer.FlushAsync(cancellationToken);

            return new FileFormatWriteResult { Success = true };
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

    private static Dictionary<string, string> ParseKeyValue(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';'))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator <= 0 || separator == trimmed.Length - 1)
            {
                continue;
            }

            values[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
        }

        return values;
    }

    private static IReadOnlyList<PlsPlaylistEntry> ResolveEntries(object? payload)
    {
        if (payload is PlsPlaylistDocument document)
        {
            return document.Entries;
        }

        if (payload is IEnumerable<Dictionary<string, object?>> rows)
        {
            var entries = new List<PlsPlaylistEntry>();
            foreach (var row in rows)
            {
                var name = Read(row, "name");
                var path = Read(row, "path");
                if (string.IsNullOrWhiteSpace(path))
                {
                    path = name;
                }

                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                entries.Add(new PlsPlaylistEntry(path, string.IsNullOrWhiteSpace(name) ? null : name, ReadSize(row)));
            }

            return entries;
        }

        throw new InvalidOperationException("PLS payload must be a PLS playlist document or catalog rows.");
    }

    private static long ReadSize(Dictionary<string, object?> row)
    {
        foreach (var key in new[] { "size", "sizeBytes" })
        {
            var text = Read(row, key);
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return 0L;
    }

    private static string Read(Dictionary<string, object?> row, string key)
    {
        if (row.TryGetValue(key, out var value))
        {
            return value?.ToString() ?? string.Empty;
        }

        foreach (var (candidate, candidateValue) in row)
        {
            if (candidate.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return candidateValue?.ToString() ?? string.Empty;
            }
        }

        return string.Empty;
    }
}
