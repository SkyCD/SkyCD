using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SkyCD.App.Views;

public enum UnsavedChangesDecision
{
    Save,
    Discard,
    Cancel
}

public partial class UnsavedChangesWindow : Window
{
    public UnsavedChangesWindow()
    {
        InitializeComponent();
    }

    private void OnYesClicked(object? sender, RoutedEventArgs e)
    {
        Close(UnsavedChangesDecision.Save);
    }

    private void OnNoClicked(object? sender, RoutedEventArgs e)
    {
        Close(UnsavedChangesDecision.Discard);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(UnsavedChangesDecision.Cancel);
    }
}