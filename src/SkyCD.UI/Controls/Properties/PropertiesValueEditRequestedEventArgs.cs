using System;

namespace SkyCD.UI.Controls.Properties;

/// <summary>Raised by <see cref="PropertiesList"/> when a row asks to edit its value cell in place.</summary>
public sealed class PropertiesValueEditRequestedEventArgs(object item) : EventArgs
{
    /// <summary>The row item whose value cell should enter edit mode.</summary>
    public object Item { get; } = item;
}
