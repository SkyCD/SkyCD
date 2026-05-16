using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.M3u;

public sealed class M3uCatalogPlugin : IFileFormatPluginCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-m3u",
            DisplayName: "M3U Playlist",
            Extensions: [".m3u", ".m3u8"],
            MimeTypes: ["audio/x-mpegurl", "application/x-mpegURL"],
            CanRead: true,
            CanWrite: true);

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var reader = new StreamReader(request.Source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);
            var entries = new List<M3uPlaylistEntry>();
            var pendingTitle = default(string);
            var pendingDurationSeconds = default(int?);

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    if (!TryParseExtInf(trimmed, out var title, out var durationSeconds))
                    {
                        continue;
                    }

                    pendingTitle = title;
                    pendingDurationSeconds = durationSeconds;
                    continue;
                }

                entries.Add(new M3uPlaylistEntry(trimmed, pendingTitle, pendingDurationSeconds));
                pendingTitle = null;
                pendingDurationSeconds = null;
            }

            return new FileFormatReadResult
            {
                Success = true,
                Payload = new M3uPlaylistDocument(entries)
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
            await using var writer = new StreamWriter(request.Target, new UTF8Encoding(false), leaveOpen: true);
            await writer.WriteLineAsync("#EXTM3U");
            await writer.WriteLineAsync();

            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Title) || entry.DurationSeconds.HasValue)
                {
                    var duration = entry.DurationSeconds.GetValueOrDefault(-1);
                    var title = entry.Title ?? string.Empty;
                    await writer.WriteLineAsync(FormattableString.Invariant($"#EXTINF:{duration},{title}"));
                }

                await writer.WriteLineAsync(entry.Path);
                await writer.WriteLineAsync();
            }

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

    private static bool TryParseExtInf(string value, out string? title, out int? durationSeconds)
    {
        title = null;
        durationSeconds = null;
        if (!value.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payload = value["#EXTINF:".Length..];
        var commaIndex = payload.IndexOf(',');
        if (commaIndex < 0)
        {
            return false;
        }

        var durationText = payload[..commaIndex].Trim();
        var titleText = payload[(commaIndex + 1)..].Trim();
        if (int.TryParse(durationText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var duration))
        {
            durationSeconds = duration;
        }

        if (!string.IsNullOrWhiteSpace(titleText))
        {
            title = titleText;
        }

        return true;
    }

    private static IReadOnlyList<M3uPlaylistEntry> ResolveEntries(object? payload)
    {
        if (payload is M3uPlaylistDocument document)
        {
            return document.Entries;
        }

        throw new InvalidOperationException("M3U payload must be an M3U playlist document.");
    }
}

public sealed record M3uPlaylistDocument(IReadOnlyList<M3uPlaylistEntry> Entries);

public sealed record M3uPlaylistEntry(string Path, string? Title, int? DurationSeconds);
