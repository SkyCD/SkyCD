using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SkyCD.Plugin.Feature.GenDesc.Views;

public partial class DescriptionDialogView : Window
{
    public DescriptionDialogView()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
