using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Sources;

/// <summary>
/// Host surface a source-factory plugin uses to report produced files back into the open catalog. The host
/// creates the entries, refreshes the browser and enqueues metadata extraction, so the plugin never writes
/// catalog documents itself.
/// </summary>
public interface ISourceFactoryHostApi
{
    /// <summary>Gets whether a writable catalog is currently open.</summary>
    bool IsCatalogOpen { get; }

    /// <summary>Imports produced files into the open catalog and enqueues their metadata extraction.</summary>
    Task ImportSourceFilesAsync(IReadOnlyList<SourceFile> files,
        CancellationToken cancellationToken = default);

    /// <summary>Reports a user-visible message without interrupting the source.</summary>
    Task NotifyAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>Optional capability hook the host calls to give a source factory its catalog-import surface.</summary>
public interface ISourceFactoryHostConsumer : IPluginCapability
{
    /// <summary>Receives the host source-import API before the capability is activated.</summary>
    void InitializeSourceFactoryHost(ISourceFactoryHostApi host);
}
