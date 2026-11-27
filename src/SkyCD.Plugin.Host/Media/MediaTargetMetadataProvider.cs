using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Host.Media;

/// <summary>A capture tagged with the provider that produced it, so the host can key cached results.</summary>
public sealed record MediaTargetProviderCapture(
    string PluginId,
    string PluginVersion,
    string Capability,
    MediaTargetCapture Capture);

/// <summary>
/// Host seam used by the scanning and media collection flows. Implemented over the enabled plugin
/// capabilities and faked in portable tests; providers must never break a scan when a capture fails.
/// Captures are returned in provider priority order; a caller that needs every contributing provider merges
/// them, and a caller that needs one identification (a disc) takes the first successful capture.
/// </summary>
public interface IMediaTargetMetadataProvider
{
    /// <summary>Gets whether any media target provider is registered and enabled.</summary>
    bool HasProviders { get; }

    /// <summary>Captures the medium through every supporting provider; empty when none identifies it.</summary>
    Task<IReadOnlyList<MediaTargetProviderCapture>> CaptureAsync(MediaTarget target,
        CancellationToken cancellationToken);
}
