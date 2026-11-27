using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Sources;

/// <summary>
/// Capability a plugin implements to contribute an automated source type to the host's Add To List dialog.
/// The plugin owns source configuration, production loops and persistence; the host only routes user input,
/// previews the frame and imports the produced files into the catalog.
/// </summary>
public interface ISourceFactoryPluginCapability : IPluginCapability
{
    /// <summary>Stable provider identifier persisted with a saved source.</summary>
    string ProviderId { get; }

    /// <summary>Localized provider name shown in the provider picker.</summary>
    string DisplayName { get; }

    /// <summary>Gets the source kinds this provider can open.</summary>
    IReadOnlyList<SourceKind> GetSourceKinds();

    /// <summary>Gets the devices available for a device-based kind; empty when enumeration fails.</summary>
    IReadOnlyList<SourceDevice> GetDevices(string kindId);

    /// <summary>Checks that a configuration can be opened without starting a capture loop.</summary>
    Task<SourceProbeResult> ProbeAsync(SourceConfiguration configuration,
        CancellationToken cancellationToken = default);

    /// <summary>Captures one frame with the configured detector overlay so the user can verify the framing.</summary>
    Task<SourcePreviewResult> CapturePreviewAsync(SourceConfiguration configuration,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the sources this provider has saved, including sources whose capture is stopped.</summary>
    IReadOnlyList<SourceConfiguration> GetSources();

    /// <summary>Persists a new source and starts it when the plugin is active.</summary>
    Task<SourceConfiguration> AddSourceAsync(SourceDraft draft,
        CancellationToken cancellationToken = default);

    /// <summary>Stops a running source and removes its saved configuration.</summary>
    Task RemoveSourceAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>Gets the detectors this provider offers, with their parameters.</summary>
    IReadOnlyList<SourceDetectorDescriptor> GetDetectors();

    /// <summary>Gets the detector settings currently applied to sources without an override.</summary>
    SourceDetectorSettings GetDetectorSettings();

    /// <summary>Replaces the detector settings and applies them to running sources.</summary>
    Task SaveDetectorSettingsAsync(SourceDetectorSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>Raised when a saved source is added or removed.</summary>
    event EventHandler? SourcesChanged;
}
