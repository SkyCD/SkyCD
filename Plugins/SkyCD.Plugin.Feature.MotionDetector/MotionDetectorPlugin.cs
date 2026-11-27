using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using SkyCD.Plugin.Feature.MotionDetector.Capture;
using SkyCD.Plugin.Feature.MotionDetector.Configuration;
using SkyCD.Plugin.Feature.MotionDetector.Detectors;
using SkyCD.Plugin.Feature.MotionDetector.Exceptions;
using SkyCD.Plugin.Feature.MotionDetector.Models;
using SkyCD.Plugin.Feature.MotionDetector.Views;
using SkyCD.SDK.Capabilities;
using SkyCD.SDK.Capabilities.Capture;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.SDK.Capabilities.Storage;
using SkyCD.SDK.Localization;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.MotionDetector;

/// <summary>
/// Contributes motion-detector capture sources to Add To List, runs one bounded capture loop per configured
/// source, and stores sources plus detector settings in the open catalog through plugin storage.
/// </summary>
public sealed class MotionDetectorPlugin : IPluginCapability, IActivatablePluginCapability,
    ICaptureSourcePluginCapability, ICaptureSourceHostConsumer, IPluginStorageConsumer,
    IPluginConfigurationCapability, ILocalizationPluginCapability, ICatalogLifecyclePluginCapability
{
    private const string ProviderIdentifier = "motion-detector";

    private readonly object gate = new();
    private readonly SemaphoreSlim syncGate = new(1, 1);
    private readonly List<CaptureSourceConfiguration> sources = [];
    private readonly Dictionary<string, CaptureSourceRunner> runners = new(StringComparer.Ordinal);
    private CaptureDetectorSettings settings = PluginStateStore.DefaultSettings();
    private IPluginStorageApi? storage;
    private ICaptureSourceHostApi? captureHost;
    private bool activated;
    private bool suspended;
    private bool loaded;

    /// <inheritdoc />
    public event EventHandler? SourcesChanged;

    /// <inheritdoc />
    public string ProviderId => ProviderIdentifier;

    /// <inheritdoc />
    public string DisplayName => LocalizationService.Current.Get(MotionDetectorKeys.ProviderName);

    /// <summary>Gets whether sources and settings can be persisted into a currently open catalog.</summary>
    public bool IsCatalogAvailable
    {
        get
        {
            var host = captureHost;
            return host is { IsCatalogOpen: true } && storage is not null;
        }
    }

    /// <inheritdoc />
    public void InitializeStorage(IPluginStorageApi pluginStorage)
    {
        ArgumentNullException.ThrowIfNull(pluginStorage);
        storage = pluginStorage;
    }

    /// <inheritdoc />
    public void InitializeCaptureHost(ICaptureSourceHostApi host)
    {
        ArgumentNullException.ThrowIfNull(host);
        captureHost = host;
    }

    /// <inheritdoc />
    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (activated)
            {
                return;
            }

            activated = true;
        }

        await SyncRuntimeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            activated = false;
        }

        await StopAllAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void OnCatalogLifecycle(CatalogLifecycle lifecycle, string? catalogPath)
    {
        if (lifecycle == CatalogLifecycle.Closed)
        {
            suspended = true;
            _ = StopAllAsync();
            return;
        }

        if (lifecycle != CatalogLifecycle.Opened)
        {
            return;
        }

        suspended = false;
        if (Volatile.Read(ref activated))
        {
            _ = SyncRuntimeAsync();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<CaptureSourceKind> GetSourceKinds() =>
    [
        new(CaptureSourceKinds.File, Get(MotionDetectorKeys.SourceKindFile),
            Get(MotionDetectorKeys.LocationFile), UseFilePicker: true),
        new(CaptureSourceKinds.Device, Get(MotionDetectorKeys.SourceKindDevice),
            Get(MotionDetectorKeys.LocationDevice), UseDevicePicker: true),
        new(CaptureSourceKinds.JpegUrl, Get(MotionDetectorKeys.SourceKindJpegUrl), Get(MotionDetectorKeys.LocationUrl)),
        new(CaptureSourceKinds.MjpegUrl, Get(MotionDetectorKeys.SourceKindMjpegUrl), Get(MotionDetectorKeys.LocationUrl)),
        new(CaptureSourceKinds.MmsStream, Get(MotionDetectorKeys.SourceKindMmsStream), Get(MotionDetectorKeys.LocationUrl))
    ];

    /// <inheritdoc />
    public IReadOnlyList<CaptureDevice> GetDevices(string kindId) =>
        kindId == CaptureSourceKinds.Device ? DeviceEnumerator.Enumerate() : [];

    /// <inheritdoc />
    public Task<CaptureSourceProbeResult> ProbeAsync(CaptureSourceConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Task.FromResult(Probe(configuration));
    }

    private CaptureSourceProbeResult Probe(CaptureSourceConfiguration configuration)
    {
        if (!IsCatalogAvailable)
        {
            return CaptureSourceProbeResult.Unavailable(Get(MotionDetectorKeys.CatalogUnavailable));
        }

        if (string.IsNullOrWhiteSpace(configuration.DestinationFolder) ||
            !Path.IsPathFullyQualified(configuration.DestinationFolder))
        {
            return CaptureSourceProbeResult.Unavailable(Get(MotionDetectorKeys.DestinationRequired));
        }

        try
        {
            Directory.CreateDirectory(configuration.DestinationFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException)
        {
            return CaptureSourceProbeResult.Unavailable(
                LocalizationService.Current.Format(MotionDetectorKeys.DestinationMissing,
                    configuration.DestinationFolder));
        }

        if (configuration.KindId == CaptureSourceKinds.File)
        {
            return File.Exists(configuration.Location)
                ? CaptureSourceProbeResult.Available()
                : CaptureSourceProbeResult.Unavailable(Get(MotionDetectorKeys.SourceFileMissing));
        }

        if (CaptureSourceKinds.IsUrl(configuration.KindId))
        {
            return Uri.TryCreate(configuration.Location, UriKind.Absolute, out _)
                ? CaptureSourceProbeResult.Available()
                : CaptureSourceProbeResult.Unavailable(Get(MotionDetectorKeys.SourceUrlInvalid));
        }

        if (configuration.KindId == CaptureSourceKinds.Device)
        {
            return GetDevices(configuration.KindId).Any(device => device.Id == configuration.Location)
                ? CaptureSourceProbeResult.Available()
                : CaptureSourceProbeResult.Unavailable(Get(MotionDetectorKeys.SourceOpenFailed));
        }

        return CaptureSourceProbeResult.Unavailable(Get(MotionDetectorKeys.SourceOpenFailed));
    }

    /// <inheritdoc />
    public Task<CapturePreviewResult> CapturePreviewAsync(CaptureSourceConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return CapturePreviewer.CaptureAsync(configuration, GetDetectorSettings(), cancellationToken);
    }

    /// <inheritdoc />
    public IReadOnlyList<CaptureSourceConfiguration> GetSources()
    {
        lock (gate)
        {
            return sources.ToArray();
        }
    }

    /// <inheritdoc />
    public async Task<CaptureSourceConfiguration> AddSourceAsync(CaptureSourceDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var store = RequireStore();
        var label = string.IsNullOrWhiteSpace(draft.Label) ? draft.KindId : draft.Label.Trim();
        var configuration = new CaptureSourceConfiguration(
            Guid.NewGuid().ToString("N"),
            draft.KindId,
            draft.Location.Trim(),
            Path.GetFullPath(draft.DestinationFolder),
            string.IsNullOrWhiteSpace(draft.CatalogFolder) ? "Motion" : draft.CatalogFolder.Trim(),
            label);

        try
        {
            Directory.CreateDirectory(configuration.DestinationFolder);
            store.SaveSource(configuration);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException)
        {
            throw new MotionDetectorSourceException(label);
        }

        lock (gate)
        {
            sources.Add(configuration);
        }

        await SyncRuntimeAsync().ConfigureAwait(false);
        SourcesChanged?.Invoke(this, EventArgs.Empty);
        Report(LocalizationService.Current.Format(MotionDetectorKeys.SourceAdded, label));
        return configuration;
    }

    /// <inheritdoc />
    public async Task RemoveSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        var store = RequireStore();
        CaptureSourceRunner? runner = null;
        CaptureSourceConfiguration? removed = null;
        lock (gate)
        {
            if (runners.Remove(sourceId, out var existing))
            {
                runner = existing;
            }

            removed = sources.FirstOrDefault(source => source.SourceId == sourceId);
            sources.RemoveAll(source => source.SourceId == sourceId);
        }

        if (runner is not null)
        {
            await runner.StopAsync().ConfigureAwait(false);
            runner.Dispose();
        }

        store.DeleteSource(sourceId);
        SourcesChanged?.Invoke(this, EventArgs.Empty);
        if (removed is not null)
        {
            Report(LocalizationService.Current.Format(MotionDetectorKeys.SourceRemoved, removed.Label));
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<CaptureDetectorDescriptor> GetDetectors() => DetectorCatalog.Describe();

    /// <inheritdoc />
    public CaptureDetectorSettings GetDetectorSettings()
    {
        lock (gate)
        {
            return settings;
        }
    }

    /// <inheritdoc />
    public async Task SaveDetectorSettingsAsync(CaptureDetectorSettings updated,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updated);
        var store = RequireStore();
        var normalized = new CaptureDetectorSettings(
            updated.DetectorId,
            DetectorCatalog.Normalize(updated.DetectorId, updated.Parameters));
        try
        {
            store.SaveSettings(normalized);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException)
        {
            throw new MotionDetectorSettingsException();
        }

        lock (gate)
        {
            settings = normalized;
        }

        await RestartRunnersAsync().ConfigureAwait(false);
        Report(Get(MotionDetectorKeys.Saved));
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginConfigurationDescriptor> Configurations =>
        [new(ProviderIdentifier, Get(MotionDetectorKeys.OptionsPage), Order: -40)];

    /// <inheritdoc />
    public Task<PluginConfigurationPage> CreatePageAsync(PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!request.ConfigurationId.Equals(ProviderIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new PluginConfigurationPage(
                () => new TextBlock
                {
                    Text = $"Unsupported configuration '{request.ConfigurationId}'.",
                    TextWrapping = TextWrapping.Wrap
                },
                _ => Task.FromResult(new PluginConfigurationResult
                {
                    Success = false,
                    Error = $"Unsupported configuration '{request.ConfigurationId}'."
                })));
        }

        var view = new MotionDetectorOptionsView { DataContext = new MotionDetectorOptionsViewModel(this) };
        return Task.FromResult(new PluginConfigurationPage(
            () => view,
            token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new PluginConfigurationResult
                {
                    Success = true,
                    Settings = request.CurrentSettings
                });
            }));
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetTranslations() => MotionDetectorTranslations.All;

    private PluginStateStore RequireStore() =>
        storage is not null
            ? new PluginStateStore(storage)
            : throw new MotionDetectorSettingsException();

    private async Task SyncRuntimeAsync()
    {
        await syncGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!Volatile.Read(ref activated) || suspended || !IsCatalogAvailable)
            {
                await StopRunnersLockedAsync(keep: []).ConfigureAwait(false);
                return;
            }

            if (!TryLoadState())
            {
                await StopRunnersLockedAsync(keep: []).ConfigureAwait(false);
                return;
            }

            List<CaptureSourceRunner> stops = [];
            List<CaptureSourceRunner> starts = [];
            lock (gate)
            {
                foreach (var (sourceId, runner) in runners.ToArray())
                {
                    if (sources.All(source => source.SourceId != sourceId))
                    {
                        runners.Remove(sourceId);
                        stops.Add(runner);
                    }
                }

                foreach (var source in sources)
                {
                    if (runners.ContainsKey(source.SourceId))
                    {
                        continue;
                    }

                    var runner = new CaptureSourceRunner(source, GetDetectorSettings, RequireHost(), Report);
                    runners[source.SourceId] = runner;
                    starts.Add(runner);
                }
            }

            foreach (var runner in stops)
            {
                await runner.StopAsync().ConfigureAwait(false);
                runner.Dispose();
            }

            foreach (var runner in starts)
            {
                runner.Start();
            }
        }
        finally
        {
            syncGate.Release();
        }
    }

    private async Task StopAllAsync()
    {
        await syncGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopRunnersLockedAsync(keep: []).ConfigureAwait(false);
        }
        finally
        {
            syncGate.Release();
        }
    }

    private async Task RestartRunnersAsync()
    {
        await syncGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopRunnersLockedAsync(keep: []).ConfigureAwait(false);
        }
        finally
        {
            syncGate.Release();
        }

        await SyncRuntimeAsync().ConfigureAwait(false);
    }

    private async Task StopRunnersLockedAsync(IReadOnlyCollection<string> keep)
    {
        List<CaptureSourceRunner> stops = [];
        lock (gate)
        {
            foreach (var (sourceId, runner) in runners.ToArray())
            {
                if (keep.Contains(sourceId))
                {
                    continue;
                }

                runners.Remove(sourceId);
                stops.Add(runner);
            }
        }

        foreach (var runner in stops)
        {
            await runner.StopAsync().ConfigureAwait(false);
            runner.Dispose();
        }
    }

    private bool TryLoadState()
    {
        lock (gate)
        {
            if (loaded)
            {
                return true;
            }
        }

        if (storage is null)
        {
            return false;
        }

        try
        {
            var store = new PluginStateStore(storage);
            var loadedSources = store.LoadSources();
            var loadedSettings = store.LoadSettings();
            lock (gate)
            {
                sources.Clear();
                sources.AddRange(loadedSources);
                settings = loadedSettings;
                loaded = true;
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            // Storage is catalog-scoped; a closed catalog is a pause, not an error.
            return false;
        }
        catch (Exception exception)
        {
            Report(exception.Message);
            return false;
        }
    }

    private ICaptureSourceHostApi RequireHost() =>
        captureHost ?? throw new MotionDetectorSettingsException();

    private void Report(string message)
    {
        var host = captureHost;
        if (host is null)
        {
            return;
        }

        _ = host.NotifyAsync(message).ContinueWith(
            static task => _ = task.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    private static string Get(string key) => LocalizationService.Current.Get(key);
}
