using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Tar.Metadata.Models;

namespace SkyCD.Plugin.Tar.Metadata.Import;

/// <summary>
/// TAR archive metadata plugin. When the host has metadata providers enabled, it contributes the entries of
/// a TAR archive as index work items by temporarily extracting them with bounded resource usage; disposing
/// the returned set removes the extracted files.
/// </summary>
/// <remarks>
/// This is a separate plugin from the TAR open/format plugin, which lists the archive as a catalog view.
/// Discovery uses assembly metadata and implemented capability types only; the plugin exposes
/// <see cref="IIndexWorkItemProvider"/> and no file format capability, so it never competes for the
/// <c>.tar</c> extension.
/// </remarks>
public sealed class TarMetadataWorkItemProvider : IIndexWorkItemProvider
{
    private readonly TarMetadataLimits limits;

    public TarMetadataWorkItemProvider() : this(TarMetadataLimits.Default)
    {
    }

    public TarMetadataWorkItemProvider(TarMetadataLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    public Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsTarName(request.FileName) && !IsTarName(request.SourcePath))
        {
            return Task.FromResult(IndexWorkItemSet.Empty);
        }

        return TarMetadataWorkItems.CreateAsync(request, limits, cancellationToken);
    }

    private static bool IsTarName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase));
}
