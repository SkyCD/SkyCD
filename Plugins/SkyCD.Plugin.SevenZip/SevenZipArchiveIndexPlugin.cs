using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives.SevenZip;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.SevenZip;

public sealed class SevenZipArchiveIndexPlugin : IFileFormatPluginCapability
{
    private readonly ISevenZipEntryReader _entryReader;

    public SevenZipArchiveIndexPlugin() : this(new SharpCompressSevenZipEntryReader())
    {
    }

    public SevenZipArchiveIndexPlugin(ISevenZipEntryReader entryReader)
    {
        _entryReader = entryReader;
    }

    public FileFormatDescriptor SupportedFormat =>
        new FileFormatDescriptor(
            "skycd-7z",
            "7z Archive Index",
            [".7z"],
            CanRead: true,
            CanWrite: false,
            MimeType: "application/x-7z-compressed");

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "7z archive index plugin is read-only."
        });
    }

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = _entryReader.ReadEntries(request.Source);
            var rows = ProjectHierarchy(entries);

            return Task.FromResult(new FileFormatReadResult
            {
                Success = true,
                Payload = rows
            });
        }
        catch (NotSupportedException exception)
        {
            return Task.FromResult(new FileFormatReadResult
            {
                Success = false,
                Error = $"SEVENZIP_UNSUPPORTED_METHOD: {exception.Message}"
            });
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

    private static List<Dictionary<string, object?>> ProjectHierarchy(IReadOnlyCollection<SevenZipEntryInfo> entries)
    {
        var rows = new List<Dictionary<string, object?>>();
        var seenDirectories = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var normalized = entry.Path.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < parts.Length - 1; index++)
            {
                var directoryPath = string.Join("/", parts.Take(index + 1));
                if (!seenDirectories.Add(directoryPath))
                {
                    continue;
                }

                rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "folder",
                    ["fullPath"] = directoryPath,
                    ["name"] = parts[index],
                    ["sizeBytes"] = "0",
                    ["modifiedUtc"] = entry.ModifiedUtc?.ToString("O")
                });
            }

            var leafName = parts[^1];
            if (entry.IsDirectory)
            {
                if (seenDirectories.Add(normalized))
                {
                    rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["kind"] = "folder",
                        ["fullPath"] = normalized,
                        ["name"] = leafName,
                        ["sizeBytes"] = "0",
                        ["modifiedUtc"] = entry.ModifiedUtc?.ToString("O")
                    });
                }
            }
            else
            {
                rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "file",
                    ["fullPath"] = normalized,
                    ["name"] = leafName,
                    ["sizeBytes"] = entry.SizeBytes.ToString(),
                    ["modifiedUtc"] = entry.ModifiedUtc?.ToString("O")
                });
            }
        }

        return rows
            .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
            .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
            .ToList();
    }
}

public interface ISevenZipEntryReader
{
    IReadOnlyCollection<SevenZipEntryInfo> ReadEntries(Stream source);
}

public sealed record SevenZipEntryInfo(
    string Path,
    bool IsDirectory,
    long SizeBytes,
    DateTime? ModifiedUtc);

