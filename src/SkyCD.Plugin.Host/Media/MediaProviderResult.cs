using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Host.Media;

public sealed record MediaProviderResult(string PluginId, string PluginVersion, string Capability,
    MediaMetadataSchema? Schema, MediaExtractionResult? Metadata, MediaPreviewResult? Preview,
    bool Failed, bool TimedOut);

public sealed record MediaExecutionLimits(long SourceBytes = 16 * 1024 * 1024,
    long ArtifactBytes = 4 * 1024 * 1024, int Concurrency = 2, int TimeoutMilliseconds = 5000);
