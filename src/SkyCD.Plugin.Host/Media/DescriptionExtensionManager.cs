using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Host.Media;

/// <summary>An AI description implementation offered in the Options picker.</summary>
public sealed record AiDescriptionImplementationChoice(string Id, string Name);

/// <summary>
/// Host facade for the selectable AI description capabilities. The optional capability stays absent when
/// no provider is enabled, so stored descriptions remain readable without one. When several
/// implementations are installed, the host-selected one wins and falls back to the first available.
/// </summary>
public sealed class DescriptionExtensionManager
{
    private readonly IReadOnlyList<IGeneratedDescriptionCapability> capabilities;
    private string? selectedImplementationId;

    public DescriptionExtensionManager(IEnumerable<IGeneratedDescriptionCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        this.capabilities = capabilities.ToArray();
        Capability = this.capabilities.FirstOrDefault();
    }

    /// <summary>Selectable implementations; capabilities without an identity cannot be chosen explicitly.</summary>
    public IReadOnlyList<AiDescriptionImplementationChoice> Implementations => capabilities
        .OfType<IAiDescriptionImplementationCapability>()
        .Select(static capability => new AiDescriptionImplementationChoice(
            capability.ImplementationId, capability.ImplementationName))
        .DistinctBy(static choice => choice.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>The implementation id requested by host settings.</summary>
    public string? SelectedImplementationId => selectedImplementationId;

    /// <summary>The effective implementation id, including the first-available fallback.</summary>
    public string? ActiveImplementationId => Capability is IAiDescriptionImplementationCapability identified
        ? identified.ImplementationId
        : null;

    /// <summary>The capability used for generation; never null while an implementation is installed.</summary>
    public IGeneratedDescriptionCapability? Capability { get; private set; }

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
                  capability is IAiDescriptionImplementationCapability identified &&
                  string.Equals(identified.ImplementationId, selectedImplementationId,
                      StringComparison.OrdinalIgnoreCase))
              ?? capabilities.FirstOrDefault();
    }
}
