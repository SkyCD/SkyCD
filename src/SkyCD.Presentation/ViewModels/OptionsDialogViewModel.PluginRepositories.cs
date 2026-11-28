using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Plugin.Runtime.Marketplace.Models;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Plugins settings page: the staged plug-in path and the repository list. Both are applied only when the
/// dialog is accepted, and the built-in catalog always heads the repository list.
/// </summary>
public partial class OptionsDialogViewModel
{
    public ObservableCollection<PluginRepositoryItemViewModel> PluginRepositories { get; } = [];

    [ObservableProperty] private PluginRepositoryItemViewModel? selectedPluginRepository;

    public bool IsPluginsSettingsCategorySelected => CurrentCategory.Id == PluginsSettingsCategoryId;

    public bool ShowPluginsSettingsSection =>
        IsPluginsSettingsCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    public string BuiltInCatalogUrl => PluginCatalogOptions.DefaultCatalogEndpoint.AbsoluteUri;

    /// <summary>Builds the staged list from the saved repository URLs; a built-in catalog address is not repeatable.</summary>
    public void SetPluginRepositories(IEnumerable<string>? entries)
    {
        PluginRepositories.Clear();
        PluginRepositories.Add(new PluginRepositoryItemViewModel(BuiltInCatalogUrl, isBuiltIn: true));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { BuiltInCatalogUrl };
        foreach (var entry in (entries ?? []).Where(static entry => !string.IsNullOrWhiteSpace(entry)))
        {
            var url = entry.Trim();
            // A saved copy of the built-in catalog address is a stale entry from before it moved; the
            // built-in row below already reads it, so persisting it again would only duplicate the fetch.
            if (!seen.Add(url) || IsBuiltInCatalogUrl(url))
            {
                continue;
            }

            PluginRepositories.Add(new PluginRepositoryItemViewModel(url, isBuiltIn: false));
        }

        SelectedPluginRepository = PluginRepositories[0];
    }

    /// <summary>The URLs to persist: the user's entries without the built-in catalog; empty rows are dropped.</summary>
    public IReadOnlyList<string> GetPluginRepositories()
    {
        return PluginRepositories
            .Where(static entry => !entry.IsBuiltIn)
            .Select(static entry => entry.Url.Trim())
            .Where(static url => url.Length > 0)
            .ToArray();
    }

    [RelayCommand]
    private void AddPluginRepository()
    {
        var candidate = new PluginRepositoryItemViewModel(string.Empty, isBuiltIn: false);
        PluginRepositories.Add(candidate);
        SelectedPluginRepository = candidate;
    }

    [RelayCommand(CanExecute = nameof(CanRemovePluginRepository))]
    private void RemovePluginRepository()
    {
        if (SelectedPluginRepository is not { IsBuiltIn: false } candidate)
        {
            return;
        }

        var index = PluginRepositories.IndexOf(candidate);
        PluginRepositories.Remove(candidate);
        SelectedPluginRepository = index > 0 && index < PluginRepositories.Count
            ? PluginRepositories[index]
            : PluginRepositories[^1];
    }

    /// <summary>Applies catalog states to the staged rows; rows that are not part of the snapshot stay unknown.</summary>
    public void ApplyPluginRepositoryStates(PluginCatalogSnapshot? snapshot)
    {
        foreach (var entry in PluginRepositories)
        {
            if (snapshot is null)
            {
                entry.State = PluginRepositoryState.Unknown;
                continue;
            }

            if (entry.IsBuiltIn)
            {
                entry.State = snapshot.CatalogStatus switch
                {
                    PluginCatalogStatus.Loaded => PluginRepositoryState.Ok,
                    PluginCatalogStatus.Offline => PluginRepositoryState.Offline,
                    _ => PluginRepositoryState.Unknown
                };
                continue;
            }

            if (!TryNormalizeRepositoryUrl(entry.Url, out var uri))
            {
                entry.State = PluginRepositoryState.Unknown;
                continue;
            }

            var source = snapshot.Sources.FirstOrDefault(candidate =>
                string.Equals(candidate.ManifestUri.AbsoluteUri, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase));
            entry.State = source?.Status switch
            {
                PluginSourceStatus.Loaded => PluginRepositoryState.Ok,
                PluginSourceStatus.Offline => PluginRepositoryState.Offline,
                PluginSourceStatus.Rejected or PluginSourceStatus.Malformed or PluginSourceStatus.UnsupportedSchema =>
                    PluginRepositoryState.Rejected,
                _ => PluginRepositoryState.Unknown
            };
        }
    }

    public void RefreshPluginRepositoryLocalization()
    {
        foreach (var entry in PluginRepositories)
        {
            entry.RefreshLocalization();
        }
    }

    private bool CanRemovePluginRepository()
    {
        return SelectedPluginRepository is { IsBuiltIn: false };
    }

    partial void OnSelectedPluginRepositoryChanged(PluginRepositoryItemViewModel? value)
    {
        RemovePluginRepositoryCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Validates non-empty repository rows; an invalid URL blocks the dialog and is reported inline.</summary>
    private bool TryCommitPluginRepositories()
    {
        var invalid = false;
        foreach (var entry in PluginRepositories.Where(static entry => !entry.IsBuiltIn))
        {
            var url = entry.Url.Trim();
            if (url.Length == 0)
            {
                entry.Error = string.Empty;
                continue;
            }

            if (!TryNormalizeRepositoryUrl(url, out var uri))
            {
                entry.Error = LocalizationService.Current.Get(UiKeys.PluginRepositoryInvalidUrl);
                invalid = true;
                continue;
            }

            if (PluginCatalogOptions.IsBuiltInCatalogEndpoint(uri))
            {
                entry.Error = LocalizationService.Current.Get(UiKeys.PluginRepositoryBuiltInUrl);
                invalid = true;
                continue;
            }

            entry.Url = uri.AbsoluteUri;
            entry.Error = string.Empty;
        }

        if (!invalid)
        {
            return true;
        }

        SettingsSearchText = string.Empty;
        SelectedSettingCategory = SettingCategories.First(category => category.Id == PluginsSettingsCategoryId);
        return false;
    }

    private static bool TryNormalizeRepositoryUrl(string text, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || !parsed.IsAbsoluteUri)
        {
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(parsed.UserInfo))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static bool IsBuiltInCatalogUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && PluginCatalogOptions.IsBuiltInCatalogEndpoint(uri);
    }
}
