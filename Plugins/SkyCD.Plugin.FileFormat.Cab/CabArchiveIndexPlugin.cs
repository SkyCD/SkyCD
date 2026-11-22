using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.FileFormat.Cab.Import;
using SkyCD.Plugin.FileFormat.Cab.Models;

namespace SkyCD.Plugin.FileFormat.Cab;

/// <summary>
/// Read-only Microsoft Cabinet (CAB) archive plugin. Lists archive entries as a catalog hierarchy and, when
/// the host has metadata providers enabled, hooks into the indexing sequence by contributing safely
/// extracted entries as extra work items.
/// </summary>
public sealed class CabArchiveIndexPlugin : IFileFormatPluginCapability, IIndexWorkItemProvider
{
    private const string CabFormatId = "skycd-cab";
    private readonly CabIndexLimits limits;

    public CabArchiveIndexPlugin() : this(CabIndexLimits.Default)
    {
    }

    public CabArchiveIndexPlugin(CabIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: CabFormatId,
            DisplayName: "Cabinet Archive Index",
            Extensions: [".cab"],
            MimeTypes: ["application/vnd.ms-cab-compressed", "application/x-cab"],
            CanRead: true,
            CanWrite: false);

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(Path.GetExtension(request.FileName), ".cab", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return CabIndexWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "Cabinet archive index plugin is read-only."
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

            var (success, error, rows) = await CabIndexWorkItems
                .ListAsync(source, request.Progress, cancellationToken)
                .ConfigureAwait(false);

            return success
                ? new FileFormatReadResult
                {
                    Success = true,
                    Payload = rows
                }
                : Failure(error ?? "CAB_DAMAGED: The cabinet archive could not be read.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException
                                              or NotSupportedException)
        {
            return Failure($"CAB_DAMAGED: {exception.Message}");
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
}
