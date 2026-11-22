using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.FileFormat.Ace.Exceptions;
using SkyCD.Plugin.FileFormat.Ace.Import;
using SkyCD.Plugin.FileFormat.Ace.Models;
using SkyCD.Plugin.FileFormat.Ace.Parsing;

namespace SkyCD.Plugin.FileFormat.Ace;

/// <summary>
/// Read-only ACE archive plugin. Lists archive entries as a catalog hierarchy from the checksummed ACE
/// headers and, when the host has metadata providers enabled, contributes every stored (method 0) entry
/// whose CRC-16 verifies as an extra work item. Compressed ACE methods are listed but never decompressed,
/// because the format is proprietary and no license-compatible decompressor exists.
/// </summary>
public sealed class AceArchiveIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private const string AceFormatId = "skycd-ace";
    private readonly AceIndexLimits limits;

    public AceArchiveIndexPlugin() : this(AceIndexLimits.Default)
    {
    }

    public AceArchiveIndexPlugin(AceIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: AceFormatId,
            DisplayName: "ACE Archive Index",
            Extensions: [".ace"],
            MimeTypes: ["application/x-ace-compressed", "application/x-ace"],
            CanRead: true,
            CanWrite: false);

    /// <summary>Gets the typed diagnostics for entries the last call skipped while contributing work items.</summary>
    public IReadOnlyList<string> LastDiagnostics { get; private set; } = [];

    public async Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".ace", StringComparison.OrdinalIgnoreCase))
        {
            LastDiagnostics = [];
            return IndexWorkItemSet.Empty;
        }

        var result = await AceIndexWorkItems.CreateAsync(request, limits, cancellationToken).ConfigureAwait(false);
        LastDiagnostics = result.Skipped
            .Select(entry => string.IsNullOrEmpty(entry.Name)
                ? $"{entry.Code}: {entry.Reason}"
                : $"{entry.Code}: {entry.Name}: {entry.Reason}")
            .ToList();
        return result.WorkItems;
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "ACE archive index plugin is read-only."
        });
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var source = await BufferAsync(request.Source, cancellationToken).ConfigureAwait(false);
            if (!source.CanSeek)
            {
                throw new AceArchiveFormatException("the source cannot be read.");
            }

            var headers = new AceHeaderReader().Read(source, cancellationToken);
            request.Progress?.Report(100);

            return new FileFormatReadResult
            {
                Success = true,
                Payload = ProjectHierarchy(headers)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AceEncryptedArchiveException exception)
        {
            return Failure($"ACE_ENCRYPTED: {exception.Message}");
        }
        catch (AceUnsupportedBlockException exception)
        {
            return Failure($"ACE_UNSUPPORTED: {exception.Message}");
        }
        catch (Exception exception) when (AceArchiveFailures.IsArchiveFailure(exception))
        {
            return Failure($"ACE_DAMAGED: {exception.Message}");
        }
    }

    private static async Task<Stream> BufferAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source.CanSeek)
        {
            return source;
        }

        var buffered = new MemoryStream();
        await source.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
        buffered.Position = 0;
        return buffered;
    }

    private static List<Dictionary<string, object?>> ProjectHierarchy(IEnumerable<AceHeader> headers)
    {
        var rows = new List<Dictionary<string, object?>>();
        var seenDirectories = new HashSet<string>(StringComparer.Ordinal);

        foreach (var header in headers)
        {
            if (header.BlockType != AceBlockType.File || header.IsEncrypted)
            {
                continue;
            }

            if (!AceEntryPath.TryNormalize(header.Name, out var normalizedPath, out _)
                || normalizedPath.Length == 0)
            {
                continue;
            }

            var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var modifiedUtc = header.ModifiedUtc?.ToString("O");
            for (var index = 0; index < parts.Length - 1; index++)
            {
                var directoryPath = string.Join("/", parts.Take(index + 1));
                if (!seenDirectories.Add(directoryPath))
                {
                    continue;
                }

                rows.Add(Row("folder", directoryPath, parts[index], 0, modifiedUtc));
            }

            var leafName = parts[^1];
            if (header.IsDirectory)
            {
                if (seenDirectories.Add(normalizedPath))
                {
                    rows.Add(Row("folder", normalizedPath, leafName, 0, modifiedUtc));
                }

                continue;
            }

            rows.Add(Row("file", normalizedPath, leafName, Math.Max(header.UnpackedSize, 0), modifiedUtc));
        }

        return rows
            .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
            .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<string, object?> Row(string kind, string fullPath, string name, long sizeBytes,
        string? modifiedUtc) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = kind,
            ["fullPath"] = fullPath,
            ["name"] = name,
            ["sizeBytes"] = sizeBytes.ToString(),
            ["modifiedUtc"] = modifiedUtc
        };

    private static FileFormatReadResult Failure(string error) => new()
    {
        Success = false,
        Error = error
    };
}
