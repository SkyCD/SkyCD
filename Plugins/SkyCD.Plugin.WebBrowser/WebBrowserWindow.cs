using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SkyCD.Plugin.WebBrowser;

internal sealed class WebBrowserWindow : Window
{
    private const string DefaultPageHtml = """
                                           <html>
                                           <head>
                                             <meta charset="utf-8" />
                                             <meta name="color-scheme" content="light dark" />
                                             <title>SkyCD Web Browser</title>
                                             <style>
                                               :root { color-scheme: light dark; }
                                               body {
                                                 font-family: Segoe UI, Arial, sans-serif;
                                                 margin: 24px;
                                                 color: #1f2937;
                                                 background: #ffffff;
                                               }
                                               h1 { margin: 0 0 12px 0; font-size: 24px; }
                                               p { margin: 8px 0; line-height: 1.4; }
                                               code { background: #f3f4f6; padding: 2px 6px; border-radius: 4px; color: #111827; }
                                               @media (prefers-color-scheme: dark) {
                                                 body {
                                                   color: #e5e7eb;
                                                   background: #111827;
                                                 }
                                                 code {
                                                   background: #1f2937;
                                                   color: #f9fafb;
                                                 }
                                               }
                                             </style>
                                           </head>
                                           <body>
                                             <h1>Web Browser</h1>
                                             <p>No webpage was provided.</p>
                                             <p>Enter a URL in the location bar and press Enter or click Go.</p>
                                             <p>Default target: <code>about:blank</code></p>
                                           </body>
                                           </html>
                                           """;

    private readonly NativeWebView _webView;
    private readonly TextBox _locationBox;

    private WebBrowserWindow(Uri initialUrl)
    {
        Title = "Web Browser";
        Width = 1024;
        Height = 700;
        MinWidth = 640;
        MinHeight = 420;

        _webView = new NativeWebView();
        _locationBox = new TextBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Text = initialUrl.ToString()
        };
        _locationBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                NavigateFromLocationBox();
            }
        };

        var goButton = new Button
        {
            Content = "Go",
            MinWidth = 56
        };
        goButton.Click += (_, _) => NavigateFromLocationBox();

        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(8),
            ColumnSpacing = 8
        };
        toolbar.Children.Add(_locationBox);
        toolbar.Children.Add(goButton);
        Grid.SetColumn(goButton, 1);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(_webView);
        Content = root;

        Navigate(initialUrl);
    }

    public static void ShowFor(Uri targetUrl)
    {
        var window = new WebBrowserWindow(targetUrl);
        window.Show();
    }

    private void NavigateFromLocationBox()
    {
        if (!Uri.TryCreate(_locationBox.Text, UriKind.Absolute, out var url))
        {
            return;
        }

        Navigate(url);
    }

    private void Navigate(Uri url)
    {
        _locationBox.Text = url.ToString();
        if (url.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase) &&
            url.ToString().Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            var htmlDataUrl = $"data:text/html;charset=utf-8,{Uri.EscapeDataString(DefaultPageHtml)}";
            _webView.Source = new Uri(htmlDataUrl, UriKind.Absolute);
            return;
        }

        _webView.Source = url;
    }
}
