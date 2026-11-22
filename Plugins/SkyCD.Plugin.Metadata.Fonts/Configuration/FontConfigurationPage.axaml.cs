using Avalonia.Controls;
using SkyCD.Plugin.Metadata.Fonts.Models;

namespace SkyCD.Plugin.Metadata.Fonts.Configuration;

public partial class FontConfigurationPage : UserControl
{
    private readonly FontConfigurationEditor editor;

    public FontConfigurationPage()
        : this(new FontConfigurationEditor())
    {
    }

    public FontConfigurationPage(FontConfigurationEditor editor)
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

    public void UpdateDraft()
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
    }

}
