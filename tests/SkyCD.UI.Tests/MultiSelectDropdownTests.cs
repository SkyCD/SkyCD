using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.UI.Controls.Selectors.MultiSelectDropdown;
using Xunit;

namespace SkyCD.UI.Tests;

public sealed class MultiSelectDropdownTests
{
    [AvaloniaFact]
    public void IconChoicesHideLabelsAndRemainSearchableAccessibleAndSelectable()
    {
        var star = new MultiSelectOptionItem { Label = "Star" };
        var heart = new MultiSelectOptionItem { Label = "Heart" };
        var dropdown = new MultiSelectDropdown
        {
            IsSingleSelect = true,
            ShowItemLabels = false,
            ItemsSource = [star, heart],
            ItemPreviewTemplate = new FuncDataTemplate<string>((_, _) => new Border { Width = 16, Height = 16 })
        };
        var window = new Window { Content = dropdown };
        window.Show();

        try
        {
            var search = dropdown.FindControl<TextBox>("InlineSearchTextBox")!;
            var list = dropdown.FindControl<ListBox>("ItemsListBox")!;
            search.Focus();
            search.Text = "hea";
            Dispatcher.UIThread.RunJobs();
            list.UpdateLayout();

            Assert.Same(heart, Assert.Single(list.Items));
            var choice = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            Assert.Equal("Heart", AutomationProperties.GetName(choice));
            var label = Assert.Single(choice.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "Heart");
            Assert.False(label.IsVisible);

            list.SelectedIndex = 0;
            Assert.True(heart.IsSelected);
            Assert.False(star.IsSelected);
            Assert.Equal("Heart", search.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void IconPickerShowsSelectedIconInFieldInsteadOfIdentifierText()
    {
        var dropdown = CreateIconPicker("Lucide:Star");
        var window = new Window { Content = dropdown };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            var search = dropdown.FindControl<TextBox>("InlineSearchTextBox")!;
            var preview = dropdown.FindControl<ContentControl>("SelectedIconPreview")!;

            Assert.True(preview.IsVisible);
            Assert.Equal("Lucide:Star", preview.Content);
            Assert.Equal(string.Empty, search.Text);
            Assert.Equal("Lucide:Star", AutomationProperties.GetName(search));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void IconPickerShowsPlaceholderWhenNoIconIsSelected()
    {
        var dropdown = CreateIconPicker(selectedIconKey: null);
        var window = new Window { Content = dropdown };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            var search = dropdown.FindControl<TextBox>("InlineSearchTextBox")!;
            var preview = dropdown.FindControl<ContentControl>("SelectedIconPreview")!;

            Assert.False(preview.IsVisible);
            Assert.Null(preview.Content);
            Assert.Equal("Select icon", search.Text);
            Assert.Equal("Select icon", AutomationProperties.GetName(search));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void IconPickerRemainsSearchableAndUpdatesIconWhenSelectionChanges()
    {
        var dropdown = CreateIconPicker(selectedIconKey: null);
        var window = new Window { Content = dropdown };
        window.Show();

        try
        {
            var search = dropdown.FindControl<TextBox>("InlineSearchTextBox")!;
            var preview = dropdown.FindControl<ContentControl>("SelectedIconPreview")!;
            var iconList = dropdown.FindControl<ListBox>("IconItemsListBox")!;

            search.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.False(preview.IsVisible);

            iconList.SelectedIndex = 1;
            search.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
                KeyModifiers = KeyModifiers.None
            });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Lucide:Heart", dropdown.SelectedIconKey);
            Assert.True(preview.IsVisible);
            Assert.Equal("Lucide:Heart", preview.Content);
            Assert.Equal(string.Empty, search.Text);
            Assert.Equal("Lucide:Heart", AutomationProperties.GetName(search));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClosedDropdownFieldsShowNoScrollbarInIconOrTypeMode()
    {
        var iconPicker = CreateIconPicker("Lucide:Star");
        var typePicker = new MultiSelectDropdown
        {
            ItemsSource = [new MultiSelectOptionItem { Label = "Music" }]
        };
        var window = new Window
        {
            Content = new StackPanel { Children = { iconPicker, typePicker } }
        };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            foreach (var dropdown in new[] { iconPicker, typePicker })
            {
                AssertNoVisibleScrollbar(dropdown);

                // Focus opens the popup; the inline field itself must still stay bare,
                // leaving all scrolling to the popup's list.
                dropdown.FindControl<TextBox>("InlineSearchTextBox")!.Focus();
                Dispatcher.UIThread.RunJobs();
                AssertFieldHasNoVisibleScrollbar(dropdown.FindControl<TextBox>("InlineSearchTextBox")!);
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertNoVisibleScrollbar(MultiSelectDropdown dropdown)
    {
        Assert.False(dropdown.FindControl<Popup>("DropdownPopup")!.IsOpen);
        AssertFieldHasNoVisibleScrollbar(dropdown.FindControl<TextBox>("InlineSearchTextBox")!);
    }

    private static void AssertFieldHasNoVisibleScrollbar(TextBox search)
    {
        Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.GetHorizontalScrollBarVisibility(search));
        Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.GetVerticalScrollBarVisibility(search));
        Assert.Equal(0, search.GetVisualDescendants().OfType<ScrollBar>().Count(bar => bar.IsVisible));
    }

    private static MultiSelectDropdown CreateIconPicker(string? selectedIconKey) => new()
    {
        IsIconPicker = true,
        IconKeys = ["Lucide:Star", "Lucide:Heart"],
        SelectedIconKey = selectedIconKey,
        PlaceholderText = "Select icon",
        ItemPreviewTemplate = new FuncDataTemplate<string>((_, _) => new Border { Width = 16, Height = 16 })
    };
}
