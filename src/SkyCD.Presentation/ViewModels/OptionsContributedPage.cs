using System;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.SDK;
using SkyCD.SDK.Capabilities.Options;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>One page a plugin offers to the Options dialog, with the owning plugin's identity.</summary>
public sealed record OptionsPageProvider(string PluginId, string PluginName, IOptionsPageCapability Capability);

/// <summary>
/// Hosts one plugin-contributed Options page: keeps the plugin's descriptor identity stable across navigation
/// while reading the localized title on demand, and creates the embeddable content at most once.
/// </summary>
public sealed partial class OptionsContributedPage : ObservableObject, IDisposable
{
    private readonly OptionsPageProvider provider;
    private readonly string pageId;
    private readonly string fallbackTitle;
    private readonly string? fallbackGroup;
    private object? content;
    private string error = string.Empty;
    private bool contentRequested;
    private bool disposed;

    public OptionsContributedPage(OptionsPageProvider provider)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        var descriptor = provider.Capability.OptionsPage;
        pageId = descriptor.PageId;
        Order = descriptor.Order;
        fallbackTitle = descriptor.Title;
        fallbackGroup = descriptor.Group;
    }

    public string Id => $"options:{provider.PluginId}/{pageId}";

    public string PluginId => provider.PluginId;

    public int Order { get; }

    /// <summary>
    /// Read on every access so a language changed inside Options retranslates the navigation entry; a faulty
    /// descriptor falls back to the title captured when the page was registered.
    /// </summary>
    public string Title
    {
        get
        {
            try
            {
                return provider.Capability.OptionsPage.Title;
            }
            catch (Exception exception)
            {
                // No inline surface shows a title failure, so it stays a diagnosable report — attributed
                // to the contributing capability, not to this host page.
                PluginFailure.Publish(provider.Capability.GetType(), "options", exception);
                return fallbackTitle;
            }
        }
    }

    /// <summary>Read on every access for the same retranslation reason as <see cref="Title"/>.</summary>
    public string? Group
    {
        get
        {
            try
            {
                return provider.Capability.OptionsPage.Group;
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(provider.Capability.GetType(), "options", exception);
                return fallbackGroup;
            }
        }
    }

    public object? Content
    {
        get => content;
        private set
        {
            content = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasContent));
        }
    }

    public string Error
    {
        get => error;
        private set
        {
            error = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasContent => Content is not null;

    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>Creates the plugin's view once; a failed attempt reports inline instead of retrying endlessly.</summary>
    public void EnsureContent()
    {
        if (disposed || contentRequested)
        {
            return;
        }

        contentRequested = true;
        try
        {
            var created = provider.Capability.CreateOptionsPage();
            if (created is null or Window)
            {
                (created as IDisposable)?.Dispose();
                Error = LocalizationService.Current.Format(TextKeys.PluginOptionsPageNotEmbeddable, provider.PluginName);
                return;
            }

            Content = created;
        }
        catch
        {
            // The inline error below is already the user-visible report; publishing here as well would
            // raise the global failure dialog on top of a recovered page.
            Error = LocalizationService.Current.Format(TextKeys.PluginOptionsPageUnavailable, provider.PluginName);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        var owned = content;
        content = null;
        (owned as IDisposable)?.Dispose();
    }
}
