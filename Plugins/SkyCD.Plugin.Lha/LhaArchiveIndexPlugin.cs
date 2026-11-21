using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hst.Compression.Lha;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Lha.Import;
using SkyCD.Plugin.Lha.Models;
using SkyCD.Plugin.Lha.Parsing;

namespace SkyCD.Plugin.Lha;

/// <summary>
/// Read-only LHA archive plugin. Lists archive entries as a catalog hierarchy and, when the host has
/// metadata providers enabled, hooks into the indexing sequence by contributing safely extracted entries
/// as extra work items.
/// </summary>
public sealed class LhaArchiveIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private const string LhaFormatId = "skycd-lha";
    private readonly LhaIndexLimits limits;

    public LhaArchiveIndexPlugin() : this(LhaIndexLimits.Default)
    {
    }

    public LhaArchiveIndexPlugin(LhaIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: LhaFormatId,
            DisplayName: "LHA Archive Index",
            Extensions: [".lha", ".lzh"],
            MimeTypes: ["application/x-lha", "application/x-lzh", "application/x-lzh-compressed"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsLhaFile(request.FileName))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return LhaIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "LHA archive index plugin is read-only."
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

            if (!LhaArchiveSignature.Matches(source))
            {
                return Failure("LHA_DAMAGED: Unrecognized LHA header.");
            }

            using var archive = new LhaArchive(source, LhaOptions.Default, leaveOpen: true);
            var entries = (await archive.EntriesAsync(cancellationToken).ConfigureAwait(false)).ToList();

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
        catch (NotSupportedException exception)
        {
            return Failure($"LHA_UNSUPPORTED: {exception.Message}");
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return Failure($"LHA_DAMAGED: {exception.Message}");
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    private static bool IsLhaFile(string fileName) =>
        fileName.EndsWith(".lha", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".lzh", StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectory(LhaEntry entry)
    {
        var name = entry.Name ?? string.Empty;
        if (name.EndsWith('/') || name.EndsWith('\\'))
        {
            return true;
        }

        return string.Equals(entry.Method, LhaMethod.Lhd, StringComparison.Ordinal)
               && string.IsNullOrEmpty(entry.LinkTarget);
    }

    private static List<Dictionary<string, object?>> ProjectHierarchy(IEnumerable<LhaEntry> entries)
    {
        var rows = new List<Dictionary<string, object?>>();
        var seenDirectories = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var normalizedPath = (entry.Name ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                continue;
            }

            var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var modifiedUtc = TryGetModified(entry)?.ToString("O");
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
                    ["modifiedUtc"] = modifiedUtc
                });
            }

            var leafName = parts[^1];
            if (IsDirectory(entry))
            {
                if (seenDirectories.Add(normalizedPath))
                {
                    rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["kind"] = "folder",
                        ["fullPath"] = normalizedPath,
                        ["name"] = leafName,
                        ["sizeBytes"] = "0",
                        ["modifiedUtc"] = modifiedUtc
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
                    ["sizeBytes"] = Math.Max(entry.UnpackedSize, 0).ToString(),
                    ["modifiedUtc"] = modifiedUtc
                });
            }
        }

        return rows
            .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
            .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
            .ToList();
    }

    private static DateTimeOffset? TryGetModified(LhaEntry entry)
    {
        try
        {
            return new DateTimeOffset(DateTime.SpecifyKind(entry.Date, DateTimeKind.Utc));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static FileFormatReadResult Failure(string error) => new()
    {
        Success = false,
        Error = error
    };

    private static bool IsArchiveFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
            or InvalidOperationException;
}
