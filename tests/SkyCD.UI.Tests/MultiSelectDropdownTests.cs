using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
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
}
