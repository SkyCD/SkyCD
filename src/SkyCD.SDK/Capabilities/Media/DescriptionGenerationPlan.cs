namespace SkyCD.Plugin.Abstractions.Capabilities.Media;

/// <summary>
/// Offline disclosure of what an explicit generation pass would use. Reading it never contacts the agent,
/// so a host can show the configured provider and permitted context before generation starts.
/// </summary>
public sealed record DescriptionGenerationPlan(
    bool IsEnabled,
    string ProviderName,
    string ProviderVersion,
    string? Model,
    string ProcessingDestination,
    DescriptionContextOptions ContextOptions,
    string? UnavailableReason = null);

/// <summary>
/// Optional provider-neutral disclosure for <see cref="IGeneratedDescriptionCapability"/> implementations
/// that can describe their configuration without starting a session or sending any context.
/// </summary>
public interface IDescriptionGenerationPlanner
{
    /// <summary>Reads the local configuration and reports the provider, destination and context limits.</summary>
    DescriptionGenerationPlan GetPlan();
}
