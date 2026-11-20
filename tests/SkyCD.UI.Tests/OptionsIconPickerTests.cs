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
        viewModel.SelectedTabIndex = 3;
        var window = new OptionsWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var picker = window.GetVisualDescendants().OfType<MultiSelectDropdown>()
                .First(control => control.IsIconPicker);
            var search = picker.FindControl<TextBox>("InlineSearchTextBox")!;
            var list = picker.FindControl<ListBox>("IconItemsListBox")!;
            var popup = picker.FindControl<Popup>("DropdownPopup")!;

            Assert.Equal(string.Empty, search.Text);
            var preview = picker.FindControl<ContentControl>("SelectedIconPreview")!;
            Assert.True(preview.IsVisible);
            Assert.Equal("Lucide:Check", preview.Content);
            Assert.Null(list.ItemsSource);
            Assert.True(search.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);

            search.Text = "lucide:star";
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Lucide:Star", list.Items.Cast<string>());
            Assert.All(list.Items.Cast<string>(),
                key => Assert.Contains("lucide:star", key, System.StringComparison.OrdinalIgnoreCase));

            list.SelectedItem = "Lucide:Star";
            Assert.Equal("Lucide:Star", list.SelectedItem);
            list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
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

            var search = picker.FindControl<TextBox>("InlineSearchTextBox")!;
            var list = picker.FindControl<ListBox>("IconItemsListBox")!;
            Assert.True(search.Focus());
            Dispatcher.UIThread.RunJobs();

            var item = Assert.IsType<ListBoxItem>(list.ContainerFromItem("second"));
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
}
