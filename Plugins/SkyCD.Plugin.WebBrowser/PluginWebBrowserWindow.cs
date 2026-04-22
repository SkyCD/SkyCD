using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace SkyCD.Plugin.WebBrowser;

public sealed class PluginWebBrowserWindow : Window
{
    private readonly TextBox _addressTextBox;
    private readonly ContentControl _browserHost;
    private NativeWebView? _browserView;

    public PluginWebBrowserWindow(string initialUrl)
    {
        Width = 980;
        Height = 720;
        MinWidth = 640;
        MinHeight = 420;
        Title = "Web Browser";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _addressTextBox = new TextBox
        {
            PlaceholderText = "Enter URL"
        };
        _addressTextBox.KeyDown += OnAddressKeyDown;

        var goButton = new Button
        {
            Content = "Go",
            MinWidth = 72
        };
        goButton.Click += OnGoClicked;

        var toolbarGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        toolbarGrid.Children.Add(_addressTextBox);
        Grid.SetColumn(goButton, 1);
        toolbarGrid.Children.Add(goButton);

        var toolbar = new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8),
            Child = toolbarGrid
        };

        _browserHost = new ContentControl();
        TryInitializeBrowserView(initialUrl);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(_browserHost);

        Content = root;

        Navigate(initialUrl);
    }

    private void OnGoClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Navigate(_addressTextBox.Text);
    }

    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        e.Handled = true;
        Navigate(_addressTextBox.Text);
    }

    private void Navigate(string? rawUrl)
    {
        if (_browserView is null) return;
        if (!TryNormalizeUrl(rawUrl, out var url)) return;

        _addressTextBox.Text = url.ToString();
        _browserView.Source = url;
    }

    private void TryInitializeBrowserView(string initialUrl)
    {
        try
        {
            _browserView = new NativeWebView();
            _browserHost.Content = _browserView;
            Navigate(initialUrl);
        }
        catch (Exception exception)
        {
            _browserView = null;
            _browserHost.Content = new TextBlock
            {
                Margin = new Thickness(16),
                TextWrapping = TextWrapping.Wrap,
                Text = $"Embedded browser initialization failed.{Environment.NewLine}{Environment.NewLine}{exception.Message}"
            };
        }
    }

    private static bool TryNormalizeUrl(string? rawUrl, out Uri url)
    {
        url = null!;
        if (string.IsNullOrWhiteSpace(rawUrl)) return false;

        var candidate = rawUrl.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
            candidate = $"https://{candidate}";

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed)) return false;

        url = parsed;
        return true;
    }
}
