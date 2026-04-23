using System.Formats.Tar;
using System.IO.Compression;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Tar;

public sealed class TarArchiveIndexPlugin : IPlugin, IFileFormatPluginCapability
{
    public PluginDescriptor Descriptor => new(
        "skycd.plugin.tar",
        "TAR Index Plugin",
        new Version(1, 0, 0),
        new Version(3, 0, 0),
        "Example plugin that indexes TAR archive entries.");

    public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
    [
        new FileFormatDescriptor(
            "skycd-tar",
            "TAR Archive Index",
            [".tar", ".tar.gz", ".tgz"],
            CanRead: true,
            CanWrite: false,
            MimeType: "application/x-tar")
    ];

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "TAR archive index plugin is read-only."
        });
    }

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var tarStream = EnsureTarStream(request.Source);
            var reader = new TarReader(tarStream, leaveOpen: true);
            var rows = new List<Dictionary<string, object?>>();
            var seenDirectories = new HashSet<string>(StringComparer.Ordinal);

            while (reader.GetNextEntry(copyData: false) is { } entry)
            {
                var normalizedPath = (entry.Name ?? string.Empty).Replace('\\', '/').TrimEnd('/');
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
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
                        ["modifiedUtc"] = entry.ModificationTime.UtcDateTime.ToString("O")
                    });
                }

                var isDirectory = entry.EntryType is TarEntryType.Directory;
                var leafName = parts[^1];
                if (isDirectory)
                {
                    if (seenDirectories.Add(normalizedPath))
                    {
                        rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["kind"] = "folder",
                            ["fullPath"] = normalizedPath,
                            ["name"] = leafName,
                            ["sizeBytes"] = "0",
                            ["modifiedUtc"] = entry.ModificationTime.UtcDateTime.ToString("O")
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
                        ["modifiedUtc"] = entry.ModificationTime.UtcDateTime.ToString("O")
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

    private static Stream EnsureTarStream(Stream source)
    {
        if (!source.CanSeek)
        {
            var buffered = new MemoryStream();
            source.CopyTo(buffered);
            buffered.Position = 0;
            return EnsureTarStream(buffered);
        }

        var originalPosition = source.Position;
        var header = new byte[2];
        var read = source.Read(header, 0, 2);
        source.Position = originalPosition;

        var isGzip = read == 2 && header[0] == 0x1F && header[1] == 0x8B;
        return isGzip
            ? new GZipStream(source, CompressionMode.Decompress, leaveOpen: true)
            : source;
    }
}
