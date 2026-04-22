using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SkyCD.UI.Controls;

public partial class ClassicToolbar : UserControl
{
    public ClassicToolbar()
    {
        Items = [];
        AvaloniaXamlLoader.Load(this);
    }

    public AvaloniaList<IClassicToolbarItem> Items { get; }
}