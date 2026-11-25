using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.SDK;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.Plugin.Runtime.Configuration;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>How one visited plugin draft contributes to an Options save.</summary>
public enum PluginPageSaveStatus
{
    /// <summary>The draft never loaded; it contributes nothing and does not block other settings.</summary>
    Unavailable,

    /// <summary>The loaded draft accepted its values and is ready to persist.</summary>
    Prepared,

    /// <summary>The loaded draft rejected its values; Save stops on its page to show the inline error.</summary>
    Rejected
}

/// <summary>One visited draft's contribution to an Options save.</summary>
public sealed record PluginPagePreparation(
    PluginPageSaveStatus Status,
    IReadOnlyDictionary<string, string?>? Settings = null);

/// <summary>Keeps one plugin draft alive across navigation, filtering and pending enable-state changes.</summary>
public sealed partial class OptionsPluginPage(PluginConfigurationRegistration registration,
    PluginConfigurationManager manager, TimeSpan timeout) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private Task? loadTask;
    private PluginConfigurationPage? page;
    private bool disposed;
    private bool visited;

    public PluginConfigurationRegistration Registration { get; } = registration;
    public string Id => $"plugin:{Registration.PluginId}/{Registration.Descriptor.ConfigurationId}";
    public bool IsVisited => visited;
    [ObservableProperty] private object? content;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string error = string.Empty;
    public bool HasError => !string.IsNullOrEmpty(Error);
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>Loads the draft once; a failed attempt is replaced on the next explicit call.</summary>
    public Task LoadAsync()
    {
        if (disposed || page is not null) return Task.CompletedTask;
        if (loadTask is { IsCompleted: true }) loadTask = null;
        if (loadTask is not null) return loadTask;
        visited = true;
        loadTask = LoadCoreAsync();
        return loadTask;
    }

    private async Task LoadCoreAsync()
    {
        IsLoading = true;
        Error = string.Empty;
        try
        {
            var outcome = await manager.CreatePageAsync(Registration.PluginId, timeout, lifetime.Token);
            if (!outcome.IsSuccess)
            {
                outcome.Page?.Dispose();
                SetLoadFailure(outcome);
                return;
            }

            var created = outcome.Page;
            if (created is null)
            {
                Error = LocalizationService.Current.Format(TextKeys.PluginConfigurationPageUnavailable, Registration.PluginName);
                return;
            }

            if (disposed) { created.Dispose(); return; }

            Control? view;
            try
            {
                view = created.Content as Control;
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(GetType(), "configure", exception);
                created.Dispose();
                Error = LocalizationService.Current.Format(TextKeys.PluginConfigurationPageUnavailable, Registration.PluginName);
                return;
            }

            if (view is null or Window)
            {
                created.Dispose();
                Error = LocalizationService.Current.Format(TextKeys.PluginConfigurationPageNotEmbeddable, Registration.PluginName);
                return;
            }

            page = created;
            Content = view;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            PluginFailure.Publish(GetType(), "configure", exception);
            Error = LocalizationService.Current.Format(TextKeys.PluginConfigurationPageUnavailable, Registration.PluginName);
        }
        finally { IsLoading = false; }
    }

    /// <summary>
    /// Validates the loaded draft. Unavailable drafts are skipped, a rejection stops Save, and null means
    /// the host is closing the dialog and nothing may be persisted.
    /// </summary>
    public async Task<PluginPagePreparation?> PrepareAsync()
    {
        var pending = loadTask;
        if (pending is not null) await pending;
        if (disposed) return null;
        if (page is null || Content is null) return new PluginPagePreparation(PluginPageSaveStatus.Unavailable);
        try
        {
            IsLoading = true;
            var outcome = await manager.AcceptPageAsync(Registration.PluginId, page, timeout, lifetime.Token);
            SetAcceptOutcome(outcome);
            return outcome.IsSuccess
                ? new PluginPagePreparation(PluginPageSaveStatus.Prepared, outcome.Settings)
                : new PluginPagePreparation(PluginPageSaveStatus.Rejected);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return null; }
        finally { IsLoading = false; }
    }

    private void SetLoadFailure(PluginConfigurationOutcome outcome)
    {
        Error = outcome.Status switch
        {
            PluginConfigurationStatus.TimedOut => LocalizationService.Current.Format(TextKeys.PluginConfigurationTimedOut, Registration.PluginName),
            PluginConfigurationStatus.NotSupported => LocalizationService.Current.Format(TextKeys.PluginConfigurationNotSupported, Registration.PluginName),
            _ => LocalizationService.Current.Format(TextKeys.PluginConfigurationPageUnavailable, Registration.PluginName)
        };
    }

    private void SetAcceptOutcome(PluginConfigurationOutcome outcome)
    {
        Error = outcome.Status switch
        {
            PluginConfigurationStatus.Succeeded => string.Empty,
            PluginConfigurationStatus.TimedOut => LocalizationService.Current.Format(TextKeys.PluginConfigurationTimedOut, Registration.PluginName),
            PluginConfigurationStatus.NotSupported => LocalizationService.Current.Format(TextKeys.PluginConfigurationNotSupported, Registration.PluginName),
            PluginConfigurationStatus.Canceled => LocalizationService.Current.Format(TextKeys.PluginConfigurationCanceled, Registration.PluginName),
            _ when string.IsNullOrWhiteSpace(outcome.Error) => LocalizationService.Current.Format(TextKeys.PluginConfigurationRejected, Registration.PluginName),
            _ => LocalizationService.Current.Format(TextKeys.PluginConfigurationFailed, Registration.PluginName, outcome.Error)
        };
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        // Cancellation executes plugin callbacks synchronously; a faulty callback must not
        // escape the window's Closed handler or prevent the remaining drafts being released.
        try { lifetime.Cancel(); }
        catch (Exception exception) { PluginFailure.Publish(GetType(), "configure", exception); }
        Content = null;
        try { page?.Dispose(); }
        catch (Exception exception) { PluginFailure.Publish(GetType(), "configure", exception); }
        lifetime.Dispose();
    }
}
