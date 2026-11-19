using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.UI.Controls.Selectors.MultiSelectDropdown;
using Xunit;

namespace SkyCD.UI.Tests;

public class MultiSelectDropdownTests
{
    private static MultiSelectDropdown CreateDropdown(bool hideItemLabels)
    {
        var dropdown = new MultiSelectDropdown { HideItemLabels = hideItemLabels };
        dropdown.ItemsSource =
        [
            new MultiSelectOptionItem { Label = "Star" },
            new MultiSelectOptionItem { Label = "Flag" },
        ];
        return dropdown;
    }

    private static ListBox OpenDropdown(MultiSelectDropdown dropdown)
    {
        var window = new Window { Content = dropdown };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var searchBox = Assert.IsType<TextBox>(dropdown.FindControl<TextBox>("InlineSearchTextBox"));
        searchBox.Focus();
        Dispatcher.UIThread.RunJobs();

        var listBox = Assert.IsType<ListBox>(dropdown.FindControl<ListBox>("ItemsListBox"));
        Assert.NotEmpty(listBox.GetVisualDescendants().OfType<ListBoxItem>());
        return listBox;
    }

    private static TextBlock FindNameText(ListBoxItem row, string label)
    {
        var nameText = row.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(text => text.Text == label);
        return Assert.IsType<TextBlock>(nameText);
    }

    [AvaloniaFact]
    public void HideItemLabels_HidesChoiceNamesInDropdown()
    {
        var listBox = OpenDropdown(CreateDropdown(hideItemLabels: true));

        foreach (var row in listBox.GetVisualDescendants().OfType<ListBoxItem>())
        {
            var label = Assert.IsType<MultiSelectOptionItem>(row.DataContext).Label;
            Assert.False(FindNameText(row, label).IsVisible);
        }
    }

    [AvaloniaFact]
    public void ChoiceNames_AreVisibleByDefault()
    {
        var listBox = OpenDropdown(CreateDropdown(hideItemLabels: false));

        foreach (var row in listBox.GetVisualDescendants().OfType<ListBoxItem>())
        {
            var label = Assert.IsType<MultiSelectOptionItem>(row.DataContext).Label;
            Assert.True(FindNameText(row, label).IsVisible);
        }
    }

    [AvaloniaFact]
    public void HideItemLabels_SearchStillFiltersChoicesByName()
    {
        var dropdown = CreateDropdown(hideItemLabels: true);
        var listBox = OpenDropdown(dropdown);
        var searchBox = Assert.IsType<TextBox>(dropdown.FindControl<TextBox>("InlineSearchTextBox"));

        searchBox.Text = "sta";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, listBox.ItemCount);
        Assert.Equal("Star", Assert.IsType<MultiSelectOptionItem>(listBox.Items[0]).Label);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Choices_ExposeAutomationNames(bool hideItemLabels)
    {
        var listBox = OpenDropdown(CreateDropdown(hideItemLabels));

        foreach (var row in listBox.GetVisualDescendants().OfType<ListBoxItem>())
        {
            var label = Assert.IsType<MultiSelectOptionItem>(row.DataContext).Label;
            Assert.Contains(
                row.GetVisualDescendants().OfType<Border>(),
                border => AutomationProperties.GetName(border) == label);
        }
    }
}
