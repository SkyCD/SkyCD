using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Documents;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Selectors.MultiSelectDropdown;
using Xunit;

namespace SkyCD.UI.Tests;

public class OptionsIconPickerTests
{
    [AvaloniaFact]
    public void StatusIconPicker_FiltersAndUpdatesOnlyTheSelectedStatus()
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
            Assert.Contains("All", categories.Items.Cast<string>());
            Assert.Contains("Lucide", categories.Items.Cast<string>());

            search.Text = "lucide:star";
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Lucide:Star", tiles.Items.Cast<string>());
            Assert.All(tiles.Items.Cast<string>(),
                key => Assert.Contains("lucide:star", key, System.StringComparison.OrdinalIgnoreCase));

            tiles.SelectedItem = "Lucide:Star";
            Assert.Equal("Lucide:Star", tiles.SelectedItem);
            tiles.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Lucide:Star", viewModel.StatusVariants[0].IconGlyph);
            Assert.False(popup.IsOpen);
            Assert.Equal("missing:icon", viewModel.StatusVariants[1].IconGlyph);
            Assert.Equal("Lucide:Check", original[0].IconGlyph);
            Assert.Equal("Lucide:Star", viewModel.GetStatusVariants()[0].IconGlyph);
            Assert.Equal("missing:icon", viewModel.GetStatusVariants()[1].IconGlyph);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StatusIconPicker_RendersWrappingGridWithoutCheckBoxesAndWithSearchAndCategories()
    {
        var picker = CreateIconPicker("Lucide:Star", "Lucide:Star", "Lucide:Heart", "Material:Star");
        var window = new Window { Width = 400, Height = 400, Content = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            OpenPopup(picker);
            var tiles = picker.FindControl<ListBox>("IconTilesListBox")!;
            var categories = picker.FindControl<ListBox>("IconCategoryList")!;
            tiles.UpdateLayout();

            Assert.NotNull(tiles.GetVisualDescendants().OfType<WrapPanel>().FirstOrDefault());
            Assert.Empty(tiles.GetVisualDescendants().OfType<CheckBox>());
            Assert.NotNull(picker.FindControl<TextBox>("IconSearchTextBox"));
            Assert.Equal(new[] { "All", "Lucide", "Material" }, categories.Items.Cast<string>());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StatusIconPicker_PagesLargeCatalogAndLoadsMoreOnKeyboardNavigation()
    {
        var keys = Enumerable.Range(1, 300).Select(index => $"Pack:Icon{index:0000}").ToArray();
        var picker = CreateIconPicker("Pack:Icon0001", keys);
        var window = new Window { Width = 480, Height = 600, Content = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            OpenPopup(picker);
            var tiles = picker.FindControl<ListBox>("IconTilesListBox")!;
            Assert.Equal(120, tiles.Items.Count);

            tiles.SelectedIndex = 119;
            tiles.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(240, tiles.Items.Count);
            Assert.Equal(120, tiles.SelectedIndex);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StatusIconPicker_ClearingSearchRestoresTheActiveCategory()
    {
        var picker = CreateIconPicker("Lucide:Star", "Lucide:Star", "Lucide:Heart", "Material:Star");
        var window = new Window { Width = 400, Height = 400, Content = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            OpenPopup(picker);
            var search = picker.FindControl<TextBox>("IconSearchTextBox")!;
            var tiles = picker.FindControl<ListBox>("IconTilesListBox")!;
            var categories = picker.FindControl<ListBox>("IconCategoryList")!;

            categories.SelectedItem = "Lucide";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Lucide:Star", "Lucide:Heart" }, tiles.Items.Cast<string>());

            search.Text = "material";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Material:Star" }, tiles.Items.Cast<string>());

            search.Text = string.Empty;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Lucide:Star", "Lucide:Heart" }, tiles.Items.Cast<string>());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StatusIconPicker_HighlightsTheCurrentIconAndNavigatesByKeyboard()
    {
        var picker = CreateIconPicker("Lucide:Star", "Lucide:Star", "Lucide:Heart", "Material:Star");
        var window = new Window { Width = 400, Height = 400, Content = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            OpenPopup(picker);
            var search = picker.FindControl<TextBox>("IconSearchTextBox")!;
            var tiles = picker.FindControl<ListBox>("IconTilesListBox")!;
            var popup = picker.FindControl<Popup>("DropdownPopup")!;

            Assert.Equal("Lucide:Star", tiles.SelectedItem);

            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
            Dispatcher.UIThread.RunJobs();
            var focusedTile = Assert.IsType<ListBoxItem>(tiles.ContainerFromIndex(tiles.SelectedIndex));
            Assert.True(focusedTile.IsFocused);

            tiles.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
            Assert.Equal(1, tiles.SelectedIndex);

            tiles.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Lucide:Heart", picker.SelectedIconKey);
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
            SelectedIconKey = "first"
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
            var tiles = picker.FindControl<ListBox>("IconTilesListBox")!;
            Assert.True(inline.Focus());
            Dispatcher.UIThread.RunJobs();

            var item = Assert.IsType<ListBoxItem>(tiles.ContainerFromItem("second"));
            var topLevel = TopLevel.GetTopLevel(item)!;
            var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), topLevel)!.Value;
            var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            var args = new PointerPressedEventArgs(item, pointer, topLevel, point, 0, properties, KeyModifiers.None);

            item.RaiseEvent(args);
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
        PlaceholderText = "Select icon"
    };

    private static void OpenPopup(MultiSelectDropdown picker)
    {
        var inline = picker.FindControl<TextBox>("InlineSearchTextBox")!;
        Assert.True(inline.Focus());
        Dispatcher.UIThread.RunJobs();
        Assert.True(picker.FindControl<Popup>("DropdownPopup")!.IsOpen);
    }
}
