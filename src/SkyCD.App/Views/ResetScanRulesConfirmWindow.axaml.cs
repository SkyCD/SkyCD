using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SkyCD.App.Views;

public partial class ResetScanRulesConfirmWindow : Window
{
    public ResetScanRulesConfirmWindow()
    {
        InitializeComponent();
    }

    private void OnYesClicked(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnNoClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
