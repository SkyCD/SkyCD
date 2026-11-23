using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.UI.Controls.Lists;
using Xunit;

namespace SkyCD.UI.Tests;

public class BrowserRenameEditorTests
{
    private static readonly BrowserDetailsColumn[] SampleColumns =
    [
        new() { Header = "Name", ValuePath = "DisplayName", Width = new GridLength(1, GridUnitType.Star) },
        new() { Header = "Type", ValuePath = "DisplayType", Width = new GridLength(120, GridUnitType.Pixel) },
        new() { Header = "Size", ValuePath = "DisplaySize", Width = new GridLength(80, GridUnitType.Pixel) }
    ];

    [AvaloniaTheory]
    [InlineData(BrowserViewMode.Details)]
    [InlineData(BrowserViewMode.List)]
    [InlineData(BrowserViewMode.SmallIcons)]
    [InlineData(BrowserViewMode.LargeIcons)]
    [InlineData(BrowserViewMode.Tiles)]
    public void NameSwapsForRenameEditorInEveryViewMode(BrowserViewMode mode)
    {
        var item = new TestItem
        {
            Id = "row-1",
            Name = "Clip.mkv",
            DisplayName = "Clip.mkv",
            DisplayType = "Media",
            DisplaySize = "1 MB"
        };
        using var host = Show(mode, item);
        var (view, container) = (host.View, host.Container);
        var editor = FindEditor(container);

        Assert.False(editor.IsVisible);
        var label = FindLabel(container, "Clip.mkv");
        Assert.True(label.IsVisible);

        view.EditingItemId = item.Id;
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.IsVisible);
        Assert.False(label.IsVisible);

        view.RenameText = "Clip (renamed).mkv";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Clip (renamed).mkv", editor.Text);

        editor.Text = "Typed Name.mkv";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Typed Name.mkv", view.RenameText);

        view.HasRenameError = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("invalid", editor.Classes);
        view.HasRenameError = false;
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("invalid", editor.Classes);

        view.EditingItemId = null;
        Dispatcher.UIThread.RunJobs();
        Assert.False(editor.IsVisible);
        Assert.True(label.IsVisible);
    }

    [AvaloniaFact]
    public void RenameEditor_CommitsOnEnterAndCancelsOnEscapeAndFocusLoss()
    {
        var item = new TestItem { Id = "row-1", Name = "Clip.mkv", DisplayName = "Clip.mkv" };
        var view = new BrowserItemsView
        {
            ItemsSource = new List<TestItem> { item },
            DetailsColumns = SampleColumns,
            ViewMode = BrowserViewMode.List,
            Height = 150
        };
        var other = new Button { Content = "Other", Height = 24 };
        var window = new Window
        {
            Width = 400,
            Height = 200,
            Content = new StackPanel { Children = { view, other } }
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            view.EditingItemId = item.Id;
            Dispatcher.UIThread.RunJobs();
            var editor = FindEditor(FindContainer(view, item)!);

            var committed = 0;
            var cancelled = 0;
            var abandoned = 0;
            view.RenameCommitted += (_, _) => committed++;
            view.RenameCancelled += (_, _) => cancelled++;
            view.RenameAbandoned += (_, _) => abandoned++;

            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(1, committed);

            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Equal(1, cancelled);

            editor.Focus();
            Dispatcher.UIThread.RunJobs();
            other.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, abandoned);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FocusRenameEditor_TargetsTheEditingRowOnly()
    {
        var item = new TestItem { Id = "row-1", Name = "Clip.mkv", DisplayName = "Clip.mkv" };
        using var host = Show(BrowserViewMode.List, item);
        var view = host.View;

        Assert.False(view.FocusRenameEditor());

        view.EditingItemId = item.Id;
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.FocusRenameEditor());
    }

    private static BrowserHost Show(BrowserViewMode mode, TestItem item)
    {
        var view = new BrowserItemsView
        {
            ItemsSource = new List<TestItem> { item },
            DetailsColumns = SampleColumns,
            ViewMode = mode,
            Width = 400,
            Height = 200
        };
        var window = new Window { Width = 400, Height = 200, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var container = FindContainer(view, item);
        Assert.NotNull(container);
        return new BrowserHost(window, view, container!);
    }

    private static Control? FindContainer(BrowserItemsView view, object item)
    {
        if (view.ViewMode == BrowserViewMode.Details)
        {
            return view.FindControl<DetailsListView>("DetailsListView")
                ?.FindControl<ListBox>("InnerListBox")
                ?.ContainerFromItem(item);
        }

        var listBoxName = view.ViewMode == BrowserViewMode.List ? "ListModeListBox" : "IconGridListBox";
        return view.FindControl<ListBox>(listBoxName)?.ContainerFromItem(item);
    }

    private static TextBox FindEditor(Control container) =>
        container.GetVisualDescendants()
            .OfType<TextBox>()
            .Single(textBox => textBox.Classes.Contains("rename-editor"));

    private static TextBlock FindLabel(Control container, string text) =>
        container.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(textBlock => textBlock.Text == text);

    private sealed class BrowserHost(Window window, BrowserItemsView view, Control container) : IDisposable
    {
        public BrowserItemsView View { get; } = view;

        public Control Container { get; } = container;

        public void Dispose() => window.Close();
    }

    public sealed class TestItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string DisplayType { get; set; } = "";
        public string DisplaySize { get; set; } = "";
        public string IconGlyph { get; set; } = "file";
    }
}
