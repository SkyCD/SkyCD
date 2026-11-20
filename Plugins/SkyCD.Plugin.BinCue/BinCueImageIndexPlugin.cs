using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.BinCue.Exceptions;

namespace SkyCD.Plugin.BinCue;

/// <summary>
/// Read-only BIN/CUE disk-image plugin. A BIN/CUE image is a CUE text descriptor plus the raw BIN track
/// data; the plugin resolves the sheet's companion next to the sheet and lists the ISO 9660/Joliet
/// filesystem stored in its data track. When the host has metadata providers enabled, each contained
/// file is contributed as a work item materialized one at a time.
/// </summary>
public sealed class BinCueImageIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private readonly BinCueIndexLimits limits;

    public BinCueImageIndexPlugin() : this(BinCueIndexLimits.Default)
    {
    }

    public BinCueImageIndexPlugin(BinCueIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-bincue",
            DisplayName: "BIN/CUE Disk Image Index",
            Extensions: [".cue"],
            MimeTypes: ["application/x-cue", "application/x-bincue"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsCueName(request.FileName) && !IsCueName(request.SourcePath))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return BinCueIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "BIN/CUE disk image index plugin is read-only."
        });
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var sheet = await ReadSheetAsync(request, cancellationToken).ConfigureAwait(false);
            var (track, file) = BinCueDataTrack.SelectData(sheet);
            var imagePath = BinCueCompanion.ResolveImagePath(request.SourcePath, file);
            var data = BinCueDataTrack.Map(sheet, track, new FileInfo(imagePath).Length);
            var entries = BinCueFileSystems.ReadEntries(imagePath, data, limits, cancellationToken);
            request.Progress?.Report(100);

            var rows = entries
                .Select(entry => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = entry.IsDirectory ? "folder" : "file",
                    ["fullPath"] = entry.Path,
                    ["name"] = entry.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
                               ?? string.Empty,
                    ["sizeBytes"] = entry.SizeBytes.ToString(CultureInfo.InvariantCulture),
                    ["modifiedUtc"] = entry.ModifiedUtc?.UtcDateTime.ToString("O")
                                      ?? string.Empty
                })
                .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
                .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
                .ToList();

            return new FileFormatReadResult
            {
                Success = true,
                Payload = rows
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    private static async Task<CueSheet> ReadSheetAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Source is { } source && !ReferenceEquals(source, Stream.Null))
        {
            return await CueSheet.ReadAsync(source, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(request.SourcePath) && File.Exists(request.SourcePath))
        {
            await using var file = File.OpenRead(request.SourcePath);
            return await CueSheet.ReadAsync(file, cancellationToken).ConfigureAwait(false);
        }

        throw new BinCueImageFormatException("no CUE sheet stream is available.");
    }

    private static bool IsCueName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        string.Equals(Path.GetExtension(name), ".cue", StringComparison.OrdinalIgnoreCase);
}
