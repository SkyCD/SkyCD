using System.Collections.Generic;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

public sealed partial class MainMenuItemViewModel : ObservableObject
{
    public string? Key { get; init; }

    [ObservableProperty] private string header = string.Empty;

    public string? HotKey { get; init; }

    public ICommand? Command { get; init; }

    public object? CommandParameter { get; init; }

    public object? Icon { get; init; }

    public MenuItemToggleType ToggleType { get; init; }

    [ObservableProperty] private bool isChecked;

    [ObservableProperty] private bool isEnabled = true;

    [ObservableProperty] private IReadOnlyList<MainMenuItemViewModel> items = [];
}
