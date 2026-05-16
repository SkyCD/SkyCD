using Avalonia.Controls;

namespace SkyCD.Plugin.Sample.Menu;

public partial class NotificationWindow : Window
{
    public NotificationWindow()
    {
        InitializeComponent();
    }

    public NotificationWindow(string message) : this()
    {
        MessageText.Text = message;
    }

    private void OnOkClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
