using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.FileFormat.Arc.Exceptions;
using SkyCD.Plugin.FileFormat.Arc.Import;
using SkyCD.Plugin.FileFormat.Arc.Models;
using SkyCD.Plugin.FileFormat.Arc.Parsing;

namespace SkyCD.Plugin.FileFormat.Arc;

/// <summary>
/// Read-only ARC archive plugin. Lists archive entries as a catalog hierarchy and, when the host has metadata
/// providers enabled, hooks into the indexing sequence by contributing safely extracted entries as extra work
/// items. Entries whose data cannot be decoded are still listed and reported through typed diagnostics.
/// </summary>
public sealed class ArcArchiveIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private const string ArcFormatId = "skycd-arc";
    private const string ArcExtension = ".arc";
    private readonly ArcIndexLimits limits;

    public ArcArchiveIndexPlugin() : this(ArcIndexLimits.Default)
    {
    }

    public ArcArchiveIndexPlugin(ArcIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: ArcFormatId,
            DisplayName: "ARC Archive Index",
            Extensions: [ArcExtension],
            MimeTypes: ["application/x-arc", "application/x-arc-archive"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ArcExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return ArcIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "ARC archive index plugin is read-only."
        });
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
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

            var listing = ArcLister.List(source, cancellationToken);
            var rows = ArcCatalogRows.Project(listing.Entries);
            if (rows.Count == 0)
            {
                // Nothing could be listed: report the typed reason instead of an empty, successful catalog.
                var code = listing.Diagnostics.IsEmpty
                    ? DiagnosticCodes.Damaged
                    : listing.Diagnostics.Codes.First();
                return Failure($"{code}: {Describe(code)}");
            }

            request.Progress?.Report(100);

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
        catch (Exception exception) when (IsArchiveFailure(exception))
        {
            return Failure($"{DiagnosticCodes.Damaged}: {exception.Message}");
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

    private static string Describe(string code) => code switch
    {
        DiagnosticCodes.Unsupported => "Every ARC entry uses an unsupported compression method.",
        _ => "No readable ARC entries; the archive is empty, truncated, or damaged."
    };

    private static bool IsArchiveFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
            or InvalidOperationException;
}
