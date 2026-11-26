using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IconPacks.Avalonia;
using SkyCD.App.Icons;
using SkyCD.App.Views;
using SkyCD.Documents;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Selectors.MultiSelectDropdown;
using Xunit;

namespace SkyCD.UI.Tests;

public class OptionsIconPickerTests
{
    [AvaloniaFact]
    public void StatusIconPicker_SearchesTheSheetAndUpdatesOnlyTheSelectedStatus()
    {
        var original = new[]
        {
            new StatusVariantDocument { Name = "First", IconGlyph = "Lucide:Check" },
            new StatusVariantDocument { Name = "Second", IconGlyph = "missing:icon" }
        };
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetStatusVariants(original);
        viewModel.SelectedTabIndex = viewModel.SettingCategories.ToList()
            .FindIndex(category => category.Id == "statuses");
        var window = new OptionsWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var picker = window.GetVisualDescendants().OfType<MultiSelectDropdown>()
                .First(control => control.IsIconPicker);
            var inline = picker.FindControl<TextBox>("InlineSearchTextBox")!;
            var search = picker.FindControl<TextBox>("IconSearchTextBox")!;
            var tiles = picker.FindControl<ListBox>("IconTilesListBox")!;
            var categories = picker.FindControl<ListBox>("IconCategoryList")!;
            var popup = picker.FindControl<Popup>("DropdownPopup")!;

            Assert.True(inline.IsReadOnly);
            Assert.Equal(string.Empty, inline.Text);
            var preview = picker.FindControl<ContentControl>("SelectedIconPreview")!;
            Assert.True(preview.IsVisible);
            Assert.Equal("Lucide:Check", preview.Content);
            Assert.Empty(tiles.Items);

            Assert.True(inline.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            Assert.True(search.IsFocused);
            Assert.NotNull(tiles.ItemsSource);
            Assert.Contains(categories.Items.Cast<IconCategoryItem>(), item => item.Label == "Lucide");
            Assert.Contains(categories.Items.Cast<IconCategoryItem>(), item => item.Label == "Material");
            Assert.All(categories.Items.Cast<IconCategoryItem>(),
                item => Assert.True(StatusIconResolver.TryResolveKind(item.IconKey, out _)));

            search.Text = "lucide:star";
            Dispatcher.UIThread.RunJobs();
            tiles.UpdateLayout();
            Assert.Contains(GetRealizedKeys(picker), key =>
                string.Equals(key, "Lucide:Star", System.StringComparison.OrdinalIgnoreCase));
            Assert.All(GetRealizedKeys(picker),
                key => Assert.Contains("lucide:star", key, System.StringComparison.OrdinalIgnoreCase));
            var coloredIcons = GetTiles(picker).SelectMany(tile => tile.GetVisualDescendants())
                .OfType<PackIconControl>()
                .ToArray();
            Assert.NotEmpty(coloredIcons);
            Assert.All(coloredIcons, icon => Assert.NotNull(icon.Foreground));

            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
            Dispatcher.UIThread.RunJobs();
            tiles.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Lucide:Star", viewModel.StatusVariants[0].IconGlyph);
            Assert.False(popup.IsOpen);
            Assert.Equal("missing:icon", viewModel.StatusVariants[1].IconGlyph);
            Assert.Equal("Lucide:Check", original[0].IconGlyph);
            Assert.Equal("Lucide:Star", viewModel.GetStatusVariants()[0].IconGlyph);
            Assert.Equal("missing:icon", viewModel.GetStatusVariants()[1].IconGlyph);
            Assert.True(preview.IsVisible);
            Assert.Equal("Lucide:Star", preview.Content);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StatusIconPicker_RendersOneVirtualizedSheetWithIconTabsAndNoCheckBoxes()
    {
        var keys = Enumerable.Range(1, 400).Select(index => $"Pack:Icon{index:000}").ToArray();
        var picker = CreateIconPicker("Pack:Icon001", keys);
        var window = new Window { Width = 480, Height = 600, Content = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            OpenPopup(picker);
            var tiles = picker.FindControl<ListBox>("IconTilesListBox")!;
            var categories = picker.FindControl<ListBox>("IconCategoryList")!;
            var panel = picker.FindControl<Grid>("IconPickerPanel")!;
            tiles.UpdateLayout();

            Assert.Null(picker.FindControl<Button>("IconPickerCloseButton"));
            Assert.Empty(panel.GetVisualDescendants().OfType<CheckBox>());
            Assert.Empty(tiles.GetVisualDescendants().OfType<WrapPanel>());
            Assert.NotNull(picker.FindControl<TextBox>("IconSearchTextBox"));
            Assert.Single(categories.Items.Cast<IconCategoryItem>());

            var realized = GetRealizedKeys(picker);
            Assert.NotEmpty(realized);
            Assert.True(realized.Length < keys.Length,
                "the sheet must realize only the visible rows instead of every icon");
            Assert.True(GetTileRowCount(picker) > 0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StatusIconPicker_CategoryIconTabsScrollTheSheetInsteadOfFiltering()
    {
        var keys = Enumerable.Range(1, 80).Select(index => $"Alpha:Icon{index:00}")
            .Concat(Enumerable.Range(1, 80).Select(index => $"Beta:Icon{index:00}"))
            .ToArray();
        var picker = CreateIconPicker("Alpha:Icon01", keys);
        var window = new Window { Width = 480, Height = 600, Content = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            OpenPopup(picker);
            var tiles = picker.FindControl<ListBox>("IconTilesListBox")!;
            var categories = picker.FindControl<ListBox>("IconCategoryList")!;
            var popup = picker.FindControl<Popup>("DropdownPopup")!;

            Assert.Equal(new[] { "Alpha", "Beta" }, categories.Items.Cast<IconCategoryItem>().Select(item => item.Label));
            Assert.All(categories.Items.Cast<IconCategoryItem>(),
                item => Assert.StartsWith(item.Label + ":", item.IconKey));
            Assert.Equal(0, GetFirstRealizedRowStart(picker));

            var sheetHeight = tiles.Bounds.Height;
            Assert.True(sheetHeight > 0);

            categories.SelectedItem = categories.Items.Cast<IconCategoryItem>().Single(item => item.Label == "Beta");
            Dispatcher.UIThread.RunJobs();
            tiles.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(sheetHeight, tiles.Bounds.Height);
            Assert.Equal("Beta", ((IconCategoryItem)categories.SelectedItem!).Label);
            Assert.True(GetFirstRealizedRowStart(picker) > 0);
            Assert.Contains(GetRealizedKeys(picker), key => key.StartsWith("Beta:"));

            tiles.ScrollIntoView(tiles.Items[0]!);
            Dispatcher.UIThread.RunJobs();
            tiles.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Alpha", ((IconCategoryItem)categories.SelectedItem!).Label);
            Assert.True(popup.IsOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StatusIconPicker_SearchClearingRestoresTheWholeSheet()
    {
        var picker = CreateIconPicker("Lucide:Star", "Lucide:Star", "Lucide:Heart", "Material:Star");
        var window = new Window { Width = 400, Height = 400, Content = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            OpenPopup(picker);
            var search = picker.FindControl<TextBox>("IconSearchTextBox")!;

            Assert.Equal(
                new[] { "Lucide:Heart", "Lucide:Star", "Material:Star" },
                GetRealizedKeys(picker));

            search.Text = "material";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Material:Star" }, GetRealizedKeys(picker));

            search.Text = string.Empty;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                new[] { "Lucide:Heart", "Lucide:Star", "Material:Star" },
                GetRealizedKeys(picker));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StatusIconPicker_HighlightsTheCurrentIconAndNavigatesByKeyboard()
    {
        var picker = CreateIconPicker("Lucide:Heart", "Lucide:Star", "Lucide:Heart", "Material:Star");
        var window = new Window { Width = 400, Height = 400, Content = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            OpenPopup(picker);
            var search = picker.FindControl<TextBox>("IconSearchTextBox")!;
            var tiles = picker.FindControl<ListBox>("IconTilesListBox")!;
            var popup = picker.FindControl<Popup>("DropdownPopup")!;

            Assert.True(IsHighlighted(picker, "Lucide:Heart"));

            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
            Dispatcher.UIThread.RunJobs();
            Assert.True(tiles.IsFocused);

            tiles.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
            Dispatcher.UIThread.RunJobs();
            Assert.False(IsHighlighted(picker, "Lucide:Heart"));
            Assert.True(IsHighlighted(picker, "Lucide:Star"));

            tiles.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Lucide:Star", picker.SelectedIconKey);
            Assert.False(popup.IsOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StatusIconPicker_EscapeClosesThePopup()
    {
        var picker = CreateIconPicker("Lucide:Star", "Lucide:Star", "Lucide:Heart");
        var window = new Window { Width = 400, Height = 400, Content = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            OpenPopup(picker);
            var search = picker.FindControl<TextBox>("IconSearchTextBox")!;
            var popup = picker.FindControl<Popup>("DropdownPopup")!;

            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen);
            Assert.Equal("Lucide:Star", picker.SelectedIconKey);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void IconPicker_PointerPressUpdatesTheBoundKey()
    {
        var picker = new MultiSelectDropdown
        {
            IsIconPicker = true,
            ShowItemLabels = false,
            IconKeys = ["first", "second"],
            SelectedIconKey = "first",
            ItemTileTemplate = new FuncDataTemplate<string>((_, _) => new Border { Width = 16, Height = 16 })
        };
        var window = new Window { Width = 400, Height = 400, Content = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            IPointer? pointer = null;
            window.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                Avalonia.Interactivity.RoutingStrategies.Tunnel);
            window.MouseDown(new Point(10, 10), MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(new Point(10, 10), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(pointer);

            var inline = picker.FindControl<TextBox>("InlineSearchTextBox")!;
            Assert.True(inline.Focus());
            Dispatcher.UIThread.RunJobs();

            var tile = FindTile(picker, "second");
            var topLevel = TopLevel.GetTopLevel(tile)!;
            var point = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2), topLevel)!.Value;
            var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            var args = new PointerPressedEventArgs(tile, pointer, topLevel, point, 0, properties, KeyModifiers.None);

            tile.RaiseEvent(args);
            Dispatcher.UIThread.RunJobs();

            Assert.True(args.Handled);
            Assert.Equal("second", picker.SelectedIconKey);
            Assert.False(picker.FindControl<Popup>("DropdownPopup")!.IsOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MultiSelectPicker_StillTogglesTypeSelections()
    {
        var options = new[]
        {
            new MultiSelectOptionItem { Label = "File" },
            new MultiSelectOptionItem { Label = "Folder" }
        };
        var picker = new MultiSelectDropdown { ItemsSource = options };
        var window = new Window { Content = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var search = picker.FindControl<TextBox>("InlineSearchTextBox")!;
            var list = picker.FindControl<ListBox>("ItemsListBox")!;
            Assert.False(search.IsReadOnly);
            Assert.True(search.Focus());
            Dispatcher.UIThread.RunJobs();
            list.SelectedIndex = 0;
            list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.True(options[0].IsSelected);
            Assert.False(options[1].IsSelected);
        }
        finally
        {
            window.Close();
        }
    }

    private static MultiSelectDropdown CreateIconPicker(string? selectedIconKey, params string[] keys) => new()
    {
        IsIconPicker = true,
        IconKeys = keys,
        SelectedIconKey = selectedIconKey,
        PlaceholderText = "Select icon",
        ItemTileTemplate = new FuncDataTemplate<string>((_, _) => new Border { Width = 16, Height = 16 })
    };

    private static void OpenPopup(MultiSelectDropdown picker)
    {
        var inline = picker.FindControl<TextBox>("InlineSearchTextBox")!;
        Assert.True(inline.Focus());
        Dispatcher.UIThread.RunJobs();
        Assert.True(picker.FindControl<Popup>("DropdownPopup")!.IsOpen);
        picker.FindControl<ListBox>("IconTilesListBox")!.UpdateLayout();
    }

    private static Border[] GetTiles(MultiSelectDropdown picker) =>
        picker.FindControl<ListBox>("IconTilesListBox")!
            .GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("icon-tile"))
            .ToArray();

    private static string[] GetRealizedKeys(MultiSelectDropdown picker) =>
        GetTiles(picker)
            .Select(border => ((IconTileItem)border.DataContext!).Key)
            .Distinct()
            .ToArray();

    private static Border FindTile(MultiSelectDropdown picker, string key) =>
        GetTiles(picker).First(border => border.DataContext is IconTileItem tile && tile.Key == key);

    private static bool IsHighlighted(MultiSelectDropdown picker, string key) =>
        FindTile(picker, key).Classes.Contains("icon-selected");

    private static int GetTileRowCount(MultiSelectDropdown picker) =>
        picker.FindControl<ListBox>("IconTilesListBox")!.ItemCount;

    private static int GetFirstRealizedRowStart(MultiSelectDropdown picker) =>
        picker.FindControl<ListBox>("IconTilesListBox")!
            .GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Select(container => container.DataContext)
            .OfType<IconTileRow>()
            .Min(row => row.StartIndex);
}
