using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SkyCD.UI.Controls.Selectors.MultiSelectDropdown;

/// <summary>
/// One icon tile in the emoji-style sheet. The sheet is virtualized by rows, so the highlight is
/// carried by the item instead of the ListBox selection to survive container recycling.
/// </summary>
public sealed class IconTileItem : INotifyPropertyChanged
{
    private bool isHighlighted;

    public IconTileItem(string key)
    {
        Key = key;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public bool IsHighlighted
    {
        get => isHighlighted;
        set
        {
            if (isHighlighted == value)
            {
                return;
            }

            isHighlighted = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
