using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.DiskImages.BinCue.Import;
using SkyCD.Plugin.Metadata.DiskImages.Ccd.Import;
using SkyCD.Plugin.Metadata.DiskImages.Daa.Import;
using SkyCD.Plugin.Metadata.DiskImages.Dmg.Import;
using SkyCD.Plugin.Metadata.DiskImages.Iso.Import;
using SkyCD.Plugin.Metadata.DiskImages.Mdf.Import;
using SkyCD.Plugin.Metadata.DiskImages.Models;
using SkyCD.Plugin.Metadata.DiskImages.Nrg.Import;
using SkyCD.Plugin.Metadata.DiskImages.Qcow2.Import;
using SkyCD.Plugin.Metadata.DiskImages.Vdi.Import;
using SkyCD.Plugin.Metadata.DiskImages.Vhd.Import;
using SkyCD.Plugin.Metadata.DiskImages.Vmdk.Import;

namespace SkyCD.Plugin.Metadata.DiskImages;

/// <summary>
/// Disk-image metadata provider. When the host scans a path and metadata providers are enabled, it
/// contributes the files contained in supported disk images as extra work items so they can be
/// inspected. The plugin dispatches to one format reader per extension; readers materialize a single
/// entry at a time into temporary storage with bounded resource usage and clean it up on dispose.
/// </summary>
public sealed class DiskImagesIndexPlugin : IIndexWorkItemProvider
{
    private readonly DiskImageIndexLimits limits;

    public DiskImagesIndexPlugin() : this(DiskImageIndexLimits.Default)
    {
    }

    public DiskImagesIndexPlugin(DiskImageIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.Equals(Path.GetExtension(request.FileName), ".iso", StringComparison.OrdinalIgnoreCase))
        {
            return IsoIndexWorkItems.CreateAsync(request, limits, cancellationToken);
        }

        if (string.Equals(Path.GetExtension(request.FileName), ".nrg", StringComparison.OrdinalIgnoreCase))
        {
            return NrgIndexWorkItems.CreateAsync(request, limits, cancellationToken);
        }

        if (string.Equals(Path.GetExtension(request.FileName), ".daa", StringComparison.OrdinalIgnoreCase))
        {
            return DaaIndexWorkItems.CreateAsync(request, cancellationToken);
        }

        if (Matches(request, ".cue"))
        {
            return BinCueIndexWorkItems.CreateAsync(request, limits, cancellationToken);
        }

        if (Matches(request, ".ccd"))
        {
            return CcdIndexWorkItems.CreateAsync(request, limits, cancellationToken);
        }

        if (Matches(request, ".mdf") || Matches(request, ".mds"))
        {
            return MdfIndexWorkItems.CreateAsync(request, limits, cancellationToken);
        }

        if (Matches(request, ".dmg"))
        {
            return DmgIndexWorkItems.CreateAsync(request, limits, cancellationToken);
        }

        if (Matches(request, ".qcow2"))
        {
            return Qcow2IndexWorkItems.CreateAsync(request, limits, cancellationToken);
        }

        if (Matches(request, ".vdi"))
        {
            return VdiIndexWorkItems.CreateAsync(request, limits, cancellationToken);
        }

        if (Matches(request, ".vhd") || Matches(request, ".vhdx"))
        {
            return VhdIndexWorkItems.CreateAsync(request, limits, cancellationToken);
        }

        if (Matches(request, ".vmdk"))
        {
            return VmdkIndexWorkItems.CreateAsync(request, limits, cancellationToken);
        }

        return Task.FromResult(IndexWorkItemSet.Empty);
    }

    private static bool Matches(IndexWorkItemRequest request, string extension) =>
        HasExtension(request.FileName, extension) || HasExtension(request.SourcePath, extension);

    private static bool HasExtension(string? fileName, string extension) =>
        !string.IsNullOrWhiteSpace(fileName)
        && string.Equals(Path.GetExtension(fileName), extension, StringComparison.OrdinalIgnoreCase);
}
