using Avalonia.Controls;
using Avalonia.Interactivity;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Views;

public partial class AiDescriptionBatchWindow : Window
{
    public AiDescriptionBatchWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (DataContext is AiDescriptionBatchDialogViewModel { IsRunning: true } vm)
            {
                vm.CancelCommand.Execute(null);
                e.Cancel = true;
            }
        };
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AiDescriptionBatchDialogViewModel { IsRunning: true })
        {
            return;
        }

        Close();
    }
}
