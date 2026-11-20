using SkyCD.Plugin.Abstractions.Capabilities;

namespace SkyCD.Plugin.Abstractions.Capabilities.Media;

/// <summary>Stable identifiers for the AI description implementations the host can switch between.</summary>
public static class AiDescriptionImplementationIds
{
    /// <summary>Identifier of the agent-client-protocol implementation.</summary>
    public const string Acp = "acp";

    /// <summary>Identifier of the direct HTTP provider implementation.</summary>
    public const string Provider = "provider";
}

/// <summary>
/// Optionally identifies a generated-description capability so the host can let users switch between
/// installed AI description implementations.
/// </summary>
public interface IAiDescriptionImplementationCapability : IPluginCapability
{
    /// <summary>Stable identifier persisted by the host; it must not change between versions.</summary>
    string ImplementationId { get; }

    /// <summary>Short user-facing name shown in the Options implementation picker.</summary>
    string ImplementationName { get; }
}
