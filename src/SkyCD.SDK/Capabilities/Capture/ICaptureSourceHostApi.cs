using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Menu;

namespace SkyCD.SDK.Capabilities.Capture;

/// <summary>
/// Host surface a capture-source plugin uses to report captured files back into the open catalog. The host
/// creates the entries, refreshes the browser and enqueues metadata extraction, so the plugin never writes
/// catalog documents itself.
/// </summary>
public interface ICaptureSourceHostApi
{
    /// <summary>Gets whether a writable catalog is currently open.</summary>
    bool IsCatalogOpen { get; }

    /// <summary>Imports captured files into the open catalog and enqueues their metadata extraction.</summary>
    Task ImportCapturedFilesAsync(IReadOnlyList<CapturedCatalogFile> files,
        CancellationToken cancellationToken = default);

    /// <summary>Reports a user-visible message without interrupting capture.</summary>
    Task NotifyAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>Optional capability hook the host calls to give a capture plugin its catalog-import surface.</summary>
public interface ICaptureSourceHostConsumer : IPluginCapability
{
    /// <summary>Receives the host capture-import API before the capability is activated.</summary>
    void InitializeCaptureHost(ICaptureSourceHostApi host);
}
