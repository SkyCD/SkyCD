using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SkyCD.Plugin.Fonts;

public partial class FontConfigurationWindow : Window
{
    private readonly FontConfigurationEditor editor;

    public FontConfigurationWindow()
        : this(new FontConfigurationEditor())
    {
    }

    public FontConfigurationWindow(FontConfigurationEditor editor)
    {
        this.editor = editor;
        InitializeComponent();

        CollectionEnabledCheckBox.IsChecked = editor.CollectionEnabled;
        PreviewsEnabledCheckBox.IsChecked = editor.PreviewsEnabled;
        PreviewTextBox.Text = editor.PreviewText;
        PreviewSizeBox.Value = System.Math.Clamp(editor.PreviewSize, FontSettings.MinPreviewSize,
            FontSettings.MaxPreviewSize);
        PreviewLimitBox.Value = System.Math.Clamp(editor.PreviewLimitPerFolder, 0,
            FontSettings.MaxPreviewLimitPerFolder);
        ExtensionsTextBox.Text = editor.Extensions;
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        editor.CollectionEnabled = CollectionEnabledCheckBox.IsChecked == true;
        editor.PreviewsEnabled = PreviewsEnabledCheckBox.IsChecked == true;
        editor.PreviewText = PreviewTextBox.Text ?? string.Empty;
        if (PreviewSizeBox.Value is { } size)
        {
            editor.PreviewSize = (int)size;
        }

        if (PreviewLimitBox.Value is { } limit)
        {
            editor.PreviewLimitPerFolder = (int)limit;
        }

        editor.Extensions = ExtensionsTextBox.Text ?? string.Empty;
        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
