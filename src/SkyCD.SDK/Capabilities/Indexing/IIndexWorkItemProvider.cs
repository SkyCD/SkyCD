using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Indexing;

/// <summary>
/// Lets a plugin hook into the host indexing sequence by contributing extra sources, such as the files
/// stored inside an archive. The host runs its existing metadata providers over every contributed item,
/// so plugins never execute other plugins or touch the catalog.
/// </summary>
public interface IIndexWorkItemProvider : IPluginCapability
{
    /// <summary>
    /// Contributes the extra sources to index for one input source. Return an empty set when the input
    /// is not handled by this provider.
    /// </summary>
    Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default);
}
