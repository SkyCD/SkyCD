using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using SkyCD.Presentation.Scanning;
using SkyCD.UI.Localization;

namespace SkyCD.App.Views;

/// <summary>Lists the warnings and errors collected during a catalog scan.</summary>
public sealed class ScanIssuesWindow : Window
{
    public ScanIssuesWindow(IReadOnlyList<ScanIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        var localization = LocalizationService.Current;
        Title = localization.Get(UiKeys.ScanIssues);
        Width = 720;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var lines = issues.Select(FormatIssue).ToArray();
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var list = new ListBox
        {
            ItemsSource = lines,
            Height = 320,
            SelectionMode = SelectionMode.Multiple
        };
        var ok = new Button
        {
            Content = localization.Get(UiKeys.Ok),
            IsDefault = true,
            IsCancel = true
        };
        ok.Click += (_, _) => Close();
        var save = new Button { Content = localization.Get(UiKeys.ScanIssuesSaveAsText) };
        save.Click += async (_, _) =>
        {
            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = localization.Get(UiKeys.ScanIssuesSaveAsText),
                    SuggestedFileName = "scan-issues.txt",
                    DefaultExtension = "txt",
                    FileTypeChoices =
                    [
                        new FilePickerFileType(localization.Get(UiKeys.ScanIssuesSaveAsText))
                        {
                            Patterns = ["*.txt"]
                        }
                    ]
                });
                if (file is null)
                {
                    return;
                }

                await using var stream = await file.OpenWriteAsync();
                stream.SetLength(0);
                await using var writer = new StreamWriter(stream);
                foreach (var line in lines)
                {
                    await writer.WriteLineAsync(line);
                }

                status.Text = localization.Get(UiKeys.ScanIssuesSaved);
            }
            catch (Exception)
            {
                status.Text = localization.Get(UiKeys.ScanIssuesSaveFailed);
            }
        };

        Content = new DockPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Top,
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = localization.Get(UiKeys.ScanIssues),
                            FontSize = 20
                        },
                        new TextBlock
                        {
                            Text = localization.Get(UiKeys.ScanIssuesIntro),
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        list
                    }
                },
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { status, save, ok }
                }
            }
        };
    }

    private static string FormatIssue(ScanIssue issue)
    {
        var localization = LocalizationService.Current;
        var severity = localization.Get(issue.Severity == ScanIssueSeverity.Warning
            ? UiKeys.ScanIssueWarning
            : UiKeys.ScanIssueError);
        return string.IsNullOrWhiteSpace(issue.ItemPath)
            ? $"[{severity}] {issue.Message}"
            : $"[{severity}] {issue.ItemPath} — {issue.Message}";
    }
}
