using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.FileFormat.Ccd.Exceptions;
using SkyCD.Plugin.FileFormat.Ccd.Import;
using SkyCD.Plugin.FileFormat.Ccd.Models;
using SkyCD.Plugin.FileFormat.Ccd.Parsing;

namespace SkyCD.Plugin.FileFormat.Ccd;

/// <summary>
/// Read-only CloneCD disk-image plugin. A CloneCD image is a CCD descriptor plus an IMG data file, and
/// optionally a SUB subchannel file; the plugin resolves the descriptor's companions next to the
/// descriptor and lists the ISO 9660/Joliet filesystem stored in its data track. When the host has
/// metadata providers enabled, each contained file is contributed as a work item materialized one at a
/// time.
/// </summary>
public sealed class CcdImageIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private readonly CcdIndexLimits limits;

    public CcdImageIndexPlugin() : this(CcdIndexLimits.Default)
    {
    }

    public CcdImageIndexPlugin(CcdIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "skycd-ccd",
            DisplayName: "CloneCD Disk Image Index",
            Extensions: [".ccd"],
            MimeTypes: ["application/x-clonecd"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsCcdName(request.FileName) && !IsCcdName(request.SourcePath))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return CcdIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "CloneCD disk image index plugin is read-only."
        });
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var descriptor = await ReadDescriptorAsync(request, cancellationToken).ConfigureAwait(false);
            var imagePath = CcdCompanion.ResolveImagePath(request.SourcePath, descriptor);
            var track = CcdDataTrack.Resolve(descriptor, new FileInfo(imagePath).Length);
            var entries = CcdFileSystems.ReadEntries(imagePath, track, limits, cancellationToken);
            request.Progress?.Report(100);

            var rows = entries
                .Select(entry => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = entry.IsDirectory ? "folder" : "file",
                    ["fullPath"] = entry.Path,
                    ["name"] = entry.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
                               ?? string.Empty,
                    ["sizeBytes"] = entry.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
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

    private static async Task<CcdDescriptor> ReadDescriptorAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Source is { } source && !ReferenceEquals(source, Stream.Null))
        {
            return await CcdDescriptorReader.ReadAsync(source, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(request.SourcePath) && File.Exists(request.SourcePath))
        {
            await using var file = File.OpenRead(request.SourcePath);
            return await CcdDescriptorReader.ReadAsync(file, cancellationToken).ConfigureAwait(false);
        }

        throw new CcdImageFormatException("no descriptor stream is available.");
    }

    private static bool IsCcdName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        string.Equals(Path.GetExtension(name), ".ccd", StringComparison.OrdinalIgnoreCase);
}
