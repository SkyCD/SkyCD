using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SkyCD.App.Views;

public partial class LoansWindow : Window
{
    public LoansWindow() => InitializeComponent();

    private void CloseClicked(object? sender, RoutedEventArgs args) => Close();
}
