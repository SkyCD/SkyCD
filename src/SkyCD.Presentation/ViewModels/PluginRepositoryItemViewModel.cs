using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>Live state of one repository URL, taken from the catalog snapshot.</summary>
public enum PluginRepositoryState
{
    Unknown,
    Ok,
    Offline,
    Rejected
}

/// <summary>A repository manifest row on the Plugins settings page; the built-in catalog row is read-only.</summary>
public sealed partial class PluginRepositoryItemViewModel : ObservableObject
{
    [ObservableProperty] private string url;
    [ObservableProperty] private string error = string.Empty;
    [ObservableProperty] private PluginRepositoryState state;

    public PluginRepositoryItemViewModel(string url, bool isBuiltIn)
    {
        this.url = url;
        IsBuiltIn = isBuiltIn;
    }

    public bool IsBuiltIn { get; }

    public bool IsEditable => !IsBuiltIn;

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public string StateLabel => LocalizationService.Current.Get(State switch
    {
        PluginRepositoryState.Ok => UiKeys.PluginRepositoryStateOk,
        PluginRepositoryState.Offline => UiKeys.PluginRepositoryStateOffline,
        PluginRepositoryState.Rejected => UiKeys.PluginRepositoryStateRejected,
        _ => UiKeys.PluginRepositoryStateUnknown
    });

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(StateLabel));
    }

    partial void OnUrlChanged(string value)
    {
        Error = string.Empty;
    }

    partial void OnErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnStateChanged(PluginRepositoryState value)
    {
        OnPropertyChanged(nameof(StateLabel));
    }
}
