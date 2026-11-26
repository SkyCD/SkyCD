using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Host.Media;

public sealed record MediaProviderResult(string PluginId, string PluginVersion, string Capability,
    MediaMetadataSchema? Schema, MediaExtractionResult? Metadata, MediaPreviewResult? Preview,
    bool Failed, bool TimedOut);

/// <summary>
/// Per-provider execution budget. <paramref name="ProbeBytes"/> and <paramref name="ProbeTimeoutMilliseconds"/>
/// bound the optional format probe separately from extraction, so a probe never reduces the source budget.
/// </summary>
public sealed record MediaExecutionLimits(long SourceBytes = 16 * 1024 * 1024,
    long ArtifactBytes = 4 * 1024 * 1024, int Concurrency = 2, int TimeoutMilliseconds = 5000,
    int ProbeBytes = 64 * 1024, int ProbeTimeoutMilliseconds = 500);
