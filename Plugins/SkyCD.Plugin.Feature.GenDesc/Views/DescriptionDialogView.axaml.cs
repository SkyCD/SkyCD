using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SkyCD.Plugin.Feature.GenDesc.ViewModels;

namespace SkyCD.Plugin.Feature.GenDesc.Views;

public partial class DescriptionDialogView : Window
{
    public DescriptionDialogView()
    {
        InitializeComponent();
    }

    public DescriptionDialogView(string title, string text)
        : this()
    {
        DataContext = new DescriptionDialogViewModel(title, text);
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DescriptionDialogViewModel viewModel || Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(viewModel.Text);
        viewModel.MarkCopied();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
