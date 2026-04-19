using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SkyCD.UI.Controls;

public partial class FileToolbar : UserControl
{
    public static readonly StyledProperty<ICommand?> NewCommandProperty =
        AvaloniaProperty.Register<FileToolbar, ICommand?>(nameof(NewCommand));

    public static readonly StyledProperty<ICommand?> OpenCommandProperty =
        AvaloniaProperty.Register<FileToolbar, ICommand?>(nameof(OpenCommand));

    public static readonly StyledProperty<ICommand?> SaveCommandProperty =
        AvaloniaProperty.Register<FileToolbar, ICommand?>(nameof(SaveCommand));

    public FileToolbar()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public ICommand? NewCommand
    {
        get => GetValue(NewCommandProperty);
        set => SetValue(NewCommandProperty, value);
    }

    public ICommand? OpenCommand
    {
        get => GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }

    public ICommand? SaveCommand
    {
        get => GetValue(SaveCommandProperty);
        set => SetValue(SaveCommandProperty, value);
    }
}
