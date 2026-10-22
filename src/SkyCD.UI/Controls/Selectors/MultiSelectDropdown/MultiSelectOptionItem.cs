using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SkyCD.UI.Controls.Selectors.MultiSelectDropdown;

public sealed class MultiSelectOptionItem : INotifyPropertyChanged
{
    private string label = string.Empty;
    private bool isSelected;

    public string Label
    {
        get => label;
        set
        {
            if (label == value)
            {
                return;
            }

            label = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
