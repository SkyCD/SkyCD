using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Iso9660;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Iso;

public sealed class IsoImageIndexPlugin : IFileFormatPluginCapability
{
    private readonly IIsoEntryReader _entryReader;

    public IsoImageIndexPlugin() : this(new DiscUtilsIsoEntryReader())
    {
    }

    public IsoImageIndexPlugin(IIsoEntryReader entryReader)
    {
        _entryReader = entryReader;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-iso",
            DisplayName: "ISO Image Index",
            Extensions: [".iso"],
            MimeTypes: ["application/x-iso9660-image"],
            CanRead: true,
            CanWrite: false);

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "ISO image index plugin is read-only."
        });
    }

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = _entryReader.ReadEntries(request.Source);
            var rows = entries
                .Select(entry =>
                {
                    var normalizedPath = NormalizePath(entry.Path);
                    var name = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
                               ?? string.Empty;

                    return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["kind"] = entry.IsDirectory ? "folder" : "file",
                        ["fullPath"] = normalizedPath,
                        ["name"] = name,
                        ["sizeBytes"] = entry.SizeBytes.ToString(),
                        ["modifiedUtc"] = entry.ModifiedUtc?.ToString("O")
                    };
                })
                .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
                .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(new FileFormatReadResult
            {
                Success = true,
                Payload = rows
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

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(static segment =>
            {
                var versionSeparator = segment.IndexOf(';');
                var withoutVersion = versionSeparator >= 0 ? segment[..versionSeparator] : segment;
                return withoutVersion.ToUpperInvariant();
            });
        return string.Join('/', segments);
    }
}

public interface IIsoEntryReader
{
    IReadOnlyCollection<IsoEntryInfo> ReadEntries(Stream source);
}

public sealed record IsoEntryInfo(
    string Path,
    bool IsDirectory,
    long SizeBytes,
    DateTime? ModifiedUtc);
