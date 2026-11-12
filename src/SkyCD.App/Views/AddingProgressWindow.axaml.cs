using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class AddingProgressWindow : Window
{
    public AddingProgressWindow()
    {
        InitializeComponent();
    }

    private async void OnCopyLogClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddingProgressDialogViewModel vm)
        {
            var text = vm.GetFullLogText();
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (!string.IsNullOrEmpty(text) && clipboard is not null)
            {
                await clipboard.SetTextAsync(text);
            }
        }
    }
}
