using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using SkyCD.Plugin.Abstractions;

namespace SkyCD.App.Views;

/// <summary>Explicit review and consent before opening the public report submission form.</summary>
public sealed class PluginErrorWindow : Window
{
    public PluginErrorWindow(PluginFailure failure)
    {
        Title = "A plugin stopped working";
        Width = 720;
        Height = 580;
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, Height = 260 };
        var follow = new CheckBox { Content = "Include interest in future fixes (optional)" };
        var send = new Button { Content = "Review on GitHub…", IsEnabled = false };
        var save = new Button { Content = "Save diagnostics ZIP…", IsEnabled = false };
        var fixes = new Button { Content = "Find linked fixes", IsEnabled = false };
        var close = new Button { Content = "Don't send", IsCancel = true };
        close.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "The plugin could not complete the operation.", FontSize = 20 },
                new TextBlock { Text = "Review the complete report below. Nothing is sent automatically. GitHub reports are public; submit only after reviewing. Subscribe to the resulting issue on GitHub for fix notifications.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                preview, follow,
                new WrapPanel { Orientation = Orientation.Horizontal, Children = { send, save, fixes, close } },
                status
            }
        };
        try
        {
            var report = PluginErrorReport.Create(failure, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkyCD", "Diagnostics"));
            preview.Text = report.ToJson();
            follow.IsCheckedChanged += (_, _) =>
            {
                report = report with { FollowFixes = follow.IsChecked == true };
                preview.Text = report.ToJson();
            };
            send.IsEnabled = save.IsEnabled = fixes.IsEnabled = true;
            send.Click += (_, _) => Open(report.ReviewUrl);
            fixes.Click += (_, _) => Open(report.FixesUrl);
            save.Click += async (_, _) =>
            {
                try
                {
                    var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "Save safe plugin diagnostics",
                        SuggestedFileName = $"skycd-report-{report.ReportId}.zip",
                        DefaultExtension = "zip"
                    });
                    if (file is null) return;
                    await using var stream = await file.OpenWriteAsync();
                    stream.SetLength(0);
                    await report.WritePackageAsync(stream);
                    status.Text = "Diagnostics saved.";
                }
                catch (Exception) { status.Text = "Could not save diagnostics. You can retry or close this dialog."; }
            };
        }
        catch (Exception)
        {
            status.Text = "Could not prepare diagnostics. Close this dialog and try again later.";
        }

        void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception) { status.Text = "Could not open your browser. Save the diagnostics package instead."; }
        }
    }
}
