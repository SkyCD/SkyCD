using System.IO.Compression;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Zip;

public sealed class ZipArchiveIndexPlugin : IPlugin, IFileFormatPluginCapability
{
    public string Id => "skycd.plugin.zip";
    public string Name => "ZIP Index Plugin";
    public Version Version => new(1, 0, 0);
    public Version MinHostVersion => new(3, 0, 0);
    public string Description => "Example plugin that indexes ZIP archive entries.";

    public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
    [
        new FileFormatDescriptor(
            "skycd-zip",
            "ZIP Archive Index",
            [".zip"],
            CanRead: true,
            CanWrite: false,
            MimeType: "application/zip")
    ];

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "ZIP archive index plugin is read-only."
        });
    }

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var archive = new ZipArchive(request.Source, ZipArchiveMode.Read, leaveOpen: true);
            var rows = new List<Dictionary<string, object?>>();
            var seenDirectories = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in archive.Entries)
            {
                var normalizedPath = entry.FullName.Replace('\\', '/').TrimEnd('/');
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var isDirectoryEntry = entry.FullName.EndsWith("/", StringComparison.Ordinal);

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
                        ["modifiedUtc"] = entry.LastWriteTime.UtcDateTime.ToString("O")
                    });
                }

                var leafName = parts[^1];
                if (isDirectoryEntry)
                {
                    if (seenDirectories.Add(normalizedPath))
                    {
                        rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["kind"] = "folder",
                            ["fullPath"] = normalizedPath,
                            ["name"] = leafName,
                            ["sizeBytes"] = "0",
                            ["modifiedUtc"] = entry.LastWriteTime.UtcDateTime.ToString("O")
                        });
                    }
                }
                else
                {
                    rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["kind"] = "file",
                        ["fullPath"] = normalizedPath,
                        ["name"] = leafName,
                        ["sizeBytes"] = entry.Length.ToString(),
                        ["modifiedUtc"] = entry.LastWriteTime.UtcDateTime.ToString("O")
                    });
                }
            }

            var ordered = rows
                .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
                .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(new FileFormatReadResult
            {
                Success = true,
                Payload = ordered
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
}
