using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Readers.Rar;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.FileFormat.Rar.Exceptions;
using SkyCD.Plugin.FileFormat.Rar.Import;
using SkyCD.Plugin.FileFormat.Rar.Models;
using SkyCD.Plugin.FileFormat.Rar.Parsing;

namespace SkyCD.Plugin.FileFormat.Rar;

/// <summary>
/// Read-only RAR and RAR5 archive plugin. Lists archive entries as a catalog hierarchy and, when the host has
/// metadata providers enabled, hooks into the indexing sequence by contributing safely extracted entries as
/// extra work items.
/// </summary>
public sealed class RarArchiveIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private const string RarFormatId = "skycd-rar";
    private const string RarExtension = ".rar";

    private readonly RarIndexLimits limits;

    public RarArchiveIndexPlugin() : this(RarIndexLimits.Default)
    {
    }

    public RarArchiveIndexPlugin(RarIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: RarFormatId,
            DisplayName: "RAR Archive Index",
            Extensions: [RarExtension],
            MimeTypes: ["application/vnd.rar", "application/x-rar-compressed", "application/x-rar"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), RarExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return RarIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "RAR archive index plugin is read-only."
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

            if (!RarArchiveSignature.Matches(source))
            {
                return Failure(RarFailures.DamagedCode, "Unrecognized RAR signature.");
            }

            var status = RarHeaderScan.Inspect(source, RarVolumeCompanions.FindCompanion(request.FileName) is not null);
            if (status.IsMultiVolume)
            {
                return Failure(RarFailures.MultivolumeCode,
                    "The archive is part of a multi-volume set; multi-volume archives are not supported.");
            }

            if (status.IsEncrypted)
            {
                return Failure(RarFailures.EncryptedCode,
                    "The archive contains encrypted entries and cannot be listed without a password.");
            }

            if (status.Error is not null)
            {
                return new FileFormatReadResult { Success = false, Error = status.Error };
            }

            var entries = await RarHeaderScan.ListAsync(source, cancellationToken).ConfigureAwait(false);
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
        catch (Exception exception) when (RarFailures.IsArchiveFailure(exception))
        {
            return Failure(RarFailures.CodeFor(exception), exception.Message);
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    private static FileFormatReadResult Failure(string code, string message) => new()
    {
        Success = false,
        Error = RarFailures.Format(code, message)
    };

    private static List<Dictionary<string, object?>> ProjectHierarchy(IEnumerable<RarListedEntry> entries)
    {
        var rows = new List<Dictionary<string, object?>>();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
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

            var modifiedUtc = entry.ModifiedUtc?.ToString("O");
            for (var index = 0; index < parts.Length - 1; index++)
            {
                var directoryPath = string.Join("/", parts.Take(index + 1));
                if (!seenPaths.Add(directoryPath))
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
            if (!seenPaths.Add(normalizedPath))
            {
                // RAR records zero length service headers (the Quick Open cache) with the same name as the
                // file they describe; the file itself has already been listed.
                continue;
            }

            if (entry.IsDirectory)
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
            else
            {
                rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "file",
                    ["fullPath"] = normalizedPath,
                    ["name"] = leafName,
                    ["sizeBytes"] = entry.SizeBytes.ToString(),
                    ["modifiedUtc"] = modifiedUtc
                });
            }
        }

        return rows
            .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
            .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
            .ToList();
    }
}
