using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Provider-neutral, offline description of the configured AI description provider. A user-configured
/// destination is reported as declared, never as a verified privacy or cost guarantee.
/// </summary>
public sealed record AiDescriptionProviderInfo(
    bool IsPluginAvailable,
    bool IsEnabled,
    string ProviderName,
    string ProviderVersion,
    string? Model,
    string ProcessingDestination,
    DescriptionContextOptions? ContextOptions,
    string? UnavailableReason);
