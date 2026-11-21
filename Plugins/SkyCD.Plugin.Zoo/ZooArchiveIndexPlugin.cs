using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Zoo.Import;
using SkyCD.Plugin.Zoo.Models;
using SkyCD.Plugin.Zoo.Parsing;

namespace SkyCD.Plugin.Zoo;

/// <summary>
/// Read-only ZOO archive plugin. Lists archive entries as a catalog hierarchy and, when the host has
/// metadata providers enabled, hooks into the indexing sequence by contributing safely extracted entries
/// as extra work items.
/// </summary>
public sealed class ZooArchiveIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private readonly ZooIndexLimits limits;

    public ZooArchiveIndexPlugin() : this(ZooIndexLimits.Default)
    {
    }

    public ZooArchiveIndexPlugin(ZooIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-zoo",
            DisplayName: "ZOO Archive Index",
            Extensions: [".zoo"],
            MimeTypes: ["application/x-zoo", "application/zoo"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".zoo", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return ZooIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "ZOO archive index plugin is read-only."
        });
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        MemoryStream? buffered = null;
        try
        {
            var source = request.Source;
            if (!source.CanSeek)
            {
                buffered = new MemoryStream();
                await source.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
                buffered.Position = 0;
                source = buffered;
            }

            var entries = ZooArchiveReader.ReadEntries(source, limits.MaxEntries, cancellationToken);
            request.Progress?.Report(100);

            return new FileFormatReadResult
            {
                Success = true,
                Payload = ProjectHierarchy(entries)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException
                                              or NotSupportedException or ArgumentException)
        {
            return Failure($"ZOO_DAMAGED: {exception.Message}");
        }
        catch (Exception exception)
        {
            return Failure(exception.Message);
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    private static FileFormatReadResult Failure(string error) => new()
    {
        Success = false,
        Error = error
    };

    private static List<Dictionary<string, object?>> ProjectHierarchy(
        IReadOnlyCollection<ZooArchiveEntry> entries)
    {
        var rows = new List<Dictionary<string, object?>>();
        var seenDirectories = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (!ZooEntryPath.TryNormalize(entry.Path, out var normalized, out var safe) || !safe)
            {
                continue;
            }

            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

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

            rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["kind"] = "file",
                ["fullPath"] = normalized,
                ["name"] = parts[^1],
                ["sizeBytes"] = entry.OriginalBytes.ToString(),
                ["modifiedUtc"] = entry.ModifiedUtc?.ToString("O")
            });
        }

        return rows
            .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
            .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
            .ToList();
    }
}
