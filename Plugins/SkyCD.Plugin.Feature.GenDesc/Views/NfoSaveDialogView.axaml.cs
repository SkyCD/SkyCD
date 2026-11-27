using Avalonia.Controls;
using Avalonia.Interactivity;
using SkyCD.Plugin.Feature.GenDesc.ViewModels;

namespace SkyCD.Plugin.Feature.GenDesc.Views;

public partial class NfoSaveDialogView : Window
{
    public NfoSaveDialogView()
    {
        InitializeComponent();
    }

    public NfoSaveDialogView(NfoSaveDialogViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NfoSaveDialogViewModel viewModel)
        {
            return;
        }

        await viewModel.SaveAsync(TopLevel.GetTopLevel(this) is { } topLevel
            ? new AvaloniaGenDescFilePicker(topLevel)
            : null);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
