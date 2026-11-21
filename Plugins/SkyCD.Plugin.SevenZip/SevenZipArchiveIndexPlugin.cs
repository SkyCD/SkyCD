using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives.SevenZip;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.SevenZip.Import;
using SkyCD.Plugin.SevenZip.Models;
using SkyCD.Plugin.SevenZip.Parsing;

namespace SkyCD.Plugin.SevenZip;

/// <summary>
/// Read-only 7z archive plugin. Lists archive entries as a catalog hierarchy and, when the host has
/// metadata providers enabled, hooks into the indexing sequence by contributing safely extracted entries
/// as extra work items.
/// </summary>
public sealed class SevenZipArchiveIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private readonly ISevenZipEntryReader _entryReader;
    private readonly SevenZipIndexLimits limits;

    public SevenZipArchiveIndexPlugin() : this(new SharpCompressSevenZipEntryReader(), SevenZipIndexLimits.Default)
    {
    }

    public SevenZipArchiveIndexPlugin(ISevenZipEntryReader entryReader)
        : this(entryReader, SevenZipIndexLimits.Default)
    {
    }

    public SevenZipArchiveIndexPlugin(SevenZipIndexLimits limits)
        : this(new SharpCompressSevenZipEntryReader(), limits)
    {
    }

    public SevenZipArchiveIndexPlugin(ISevenZipEntryReader entryReader, SevenZipIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(entryReader);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        _entryReader = entryReader;
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-7z",
            DisplayName: "7z Archive Index",
            Extensions: [".7z"],
            MimeTypes: ["application/x-7z-compressed", "application/x-7z", "application/7z"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".7z", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return SevenZipIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "7z archive index plugin is read-only."
        });
    }

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
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
