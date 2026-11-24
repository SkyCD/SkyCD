using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SkyCD.Plugin.Feature.Locations.Views;

public partial class LocationsView : UserControl
{
    public LocationsView() => InitializeComponent();

    private void CloseClicked(object? sender, RoutedEventArgs args)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.Close();
        }
    }
}
