using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel()
    {
        NavigationItems =
        [
            new ShellNavigationItem("catalog", "Catalog", "Browse and manage indexed collections."),
            new ShellNavigationItem("plugins", "Plugins", "Inspect loaded plugins and capabilities."),
            new ShellNavigationItem("settings", "Settings", "Configure application behavior.")
        ];

        SelectedItem = NavigationItems[0];
    }

    public IReadOnlyList<ShellNavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private ShellNavigationItem selectedItem;

    public string CurrentPageTitle => SelectedItem.Title;

    public string CurrentPageDescription => SelectedItem.Description;

    [RelayCommand]
    private void Navigate(string key)
    {
        var target = NavigationItems.FirstOrDefault(item =>
            item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (target is not null)
        {
            SelectedItem = target;
        }
    }

    partial void OnSelectedItemChanged(ShellNavigationItem value)
    {
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPageDescription));
    }
}
