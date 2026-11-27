using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Host.Media;

/// <summary>An AI description implementation offered in the Options picker.</summary>
public sealed record AiDescriptionImplementationChoice(string Id, string Name);

/// <summary>
/// Host facade for the selectable description generators. The optional capability stays absent when no
/// generator is enabled, so stored descriptions remain readable without one. When several implementations
/// are installed, the host-selected one wins and falls back to the first available.
/// </summary>
public sealed class DescriptionExtensionManager
{
    private readonly IReadOnlyList<IDescriptionGeneratorCapability> capabilities;
    private string? selectedImplementationId;

    public DescriptionExtensionManager(IEnumerable<IDescriptionGeneratorCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        this.capabilities = capabilities.ToArray();
        Capability = this.capabilities.FirstOrDefault();
    }

    /// <summary>Selectable implementations, one per stable implementation id.</summary>
    public IReadOnlyList<AiDescriptionImplementationChoice> Implementations => capabilities
        .Select(static capability => new AiDescriptionImplementationChoice(
            capability.ImplementationId, capability.ImplementationName))
        .DistinctBy(static choice => choice.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>The implementation id requested by host settings.</summary>
    public string? SelectedImplementationId => selectedImplementationId;

    /// <summary>The effective implementation id, including the first-available fallback.</summary>
    public string? ActiveImplementationId => Capability?.ImplementationId;

    /// <summary>The capability used for generation; never null while an implementation is installed.</summary>
    public IDescriptionGeneratorCapability? Capability { get; private set; }

    public bool IsAvailable => Capability is not null;

    /// <summary>
    /// Requests an implementation by id. Unknown or empty ids select the first available implementation so a
    /// removed plugin never disables descriptions while another implementation is installed.
    /// </summary>
    public void SelectImplementation(string? implementationId)
    {
        selectedImplementationId = string.IsNullOrWhiteSpace(implementationId) ? null : implementationId;
        Capability = selectedImplementationId is null
            ? capabilities.FirstOrDefault()
            : capabilities.FirstOrDefault(capability =>
                  string.Equals(capability.ImplementationId, selectedImplementationId,
                      StringComparison.OrdinalIgnoreCase))
              ?? capabilities.FirstOrDefault();
    }
}
