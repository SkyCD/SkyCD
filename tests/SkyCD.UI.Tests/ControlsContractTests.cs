using SkyCD.UI.Controls;

namespace SkyCD.UI.Tests;

public class ControlsContractTests
{
    [Fact]
    public void ClassicToolbar_Items_AcceptsToolbarInterfaceTypes()
    {
        var toolbar = new ClassicToolbar();

        toolbar.Items.Add(new ClassicToolbarButton());
        toolbar.Items.Add(new ClassicToolbarSeparator());

        Assert.Equal(2, toolbar.Items.Count);
        Assert.All(toolbar.Items, item => Assert.IsAssignableFrom<IClassicToolbarItem>(item));
    }

    [Fact]
    public void PropertiesList_ProjectsDictionaryIntoRows()
    {
        var list = new PropertiesList();
        list.PropertiesData = new Dictionary<string, object?>
        {
            ["Type"] = "Audio",
            ["Size"] = 414,
            ["Location"] = "Music"
        };

        Assert.Equal(3, list.PropertiesRows.Count);
        Assert.Contains(list.PropertiesRows, row => row.Key == "Type" && row.Value == "Audio");
        Assert.Contains(list.PropertiesRows, row => row.Key == "Size" && row.Value == "414");
        Assert.Contains(list.PropertiesRows, row => row.Key == "Location" && row.Value == "Music");
    }

    [Fact]
    public void DetailsListView_ExposesGenericBindableProperties()
    {
        var view = new DetailsListView();
        var source = new[] { "a", "b" };
        var selected = "b";

        view.ItemsSource = source;
        view.SelectedItem = selected;
        view.ListMinWidth = 240;

        Assert.Equal(source, view.ItemsSource);
        Assert.Equal(selected, view.SelectedItem);
        Assert.Equal(240, view.ListMinWidth);
    }
}
