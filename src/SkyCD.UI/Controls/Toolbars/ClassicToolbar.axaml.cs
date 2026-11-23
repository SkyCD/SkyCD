using System;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SkyCD.UI.Controls.Toolbars;

public partial class ClassicToolbar : UserControl
{
    public ClassicToolbar()
    {
        Items = [];
        AvaloniaXamlLoader.Load(this);
    }

    public AvaloniaList<IClassicToolbarItem> Items { get; }

    /// <summary>
    /// Updates the checked state of the hosted button for <paramref name="commandId"/> in place, so a toggle
    /// changed from a menu item or a shortcut is reflected without rebuilding the whole toolbar.
    /// </summary>
    public void SetCheckedState(string commandId, bool isChecked)
    {
        foreach (var item in Items)
        {
            if (item is ClassicToolbarButton button
                && string.Equals(button.CommandId, commandId, StringComparison.OrdinalIgnoreCase))
            {
                button.IsChecked = isChecked;
            }
        }
    }
}
