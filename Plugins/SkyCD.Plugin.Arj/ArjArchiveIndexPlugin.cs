using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Readers.Arj;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;

namespace SkyCD.Plugin.Arj;

/// <summary>
/// Read-only ARJ archive plugin. Lists archive entries as a catalog hierarchy and, when the host has
/// metadata providers enabled, hooks into the indexing sequence by contributing safely extracted entries
/// as extra work items.
/// </summary>
public sealed class ArjArchiveIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private const string ArjFormatId = "skycd-arj";
    private readonly ArjIndexLimits limits;

    public ArjArchiveIndexPlugin() : this(ArjIndexLimits.Default)
    {
    }

    public ArjArchiveIndexPlugin(ArjIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: ArjFormatId,
            DisplayName: "ARJ Archive Index",
            Extensions: [".arj"],
            MimeTypes: ["application/x-arj", "application/arj"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".arj", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return ArjIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "ARJ archive index plugin is read-only."
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

            var rows = new List<Dictionary<string, object?>>();
            var seenDirectories = new HashSet<string>(StringComparer.Ordinal);

            await using var reader = await ArjReader
                .OpenAsyncReader(source, ReaderOptions.ForExternalStream, cancellationToken)
                .ConfigureAwait(false);

            while (await reader.MoveToNextEntryAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = reader.Entry;
                var normalizedPath = (entry.Key ?? string.Empty).Replace('\\', '/').TrimEnd('/');
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    continue;
                }

                var modifiedUtc = TryGetModified(entry);
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
                        ["modifiedUtc"] = modifiedUtc?.ToString("O")
                    });
                }

                var leafName = parts[^1];
                if (entry.IsDirectory)
                {
                    if (seenDirectories.Add(normalizedPath))
                    {
                        rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["kind"] = "folder",
                            ["fullPath"] = normalizedPath,
                            ["name"] = leafName,
                            ["sizeBytes"] = "0",
                            ["modifiedUtc"] = modifiedUtc?.ToString("O")
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
                        ["sizeBytes"] = TryGetLength(entry).ToString(),
                        ["modifiedUtc"] = modifiedUtc?.ToString("O")
                    });
                }
            }

            request.Progress?.Report(100);

            var ordered = rows
                .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
                .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
                .ToList();

            return new FileFormatReadResult
            {
                Success = true,
                Payload = ordered
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CryptographicException exception)
        {
            return Failure($"ARJ_ENCRYPTED: {exception.Message}");
        }
        catch (MultiVolumeExtractionException exception)
        {
            return Failure($"ARJ_MULTIVOLUME: {exception.Message}");
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return Failure($"ARJ_DAMAGED: {exception.Message}");
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

    private static long TryGetLength(IEntry entry)
    {
        try
        {
            return Math.Max(entry.Size, 0);
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return 0;
        }
    }

    private static DateTimeOffset? TryGetModified(IEntry entry)
    {
        try
        {
            return entry.LastModifiedTime is DateTime value
                ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
                : null;
        }
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return null;
        }
    }

    private static bool IsArchiveFailure(Exception exception) =>
        exception is SharpCompressException or IOException or NotSupportedException or ArgumentException
            or InvalidOperationException;
}
