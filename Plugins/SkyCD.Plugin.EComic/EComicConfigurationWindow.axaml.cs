using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SkyCD.Plugin.EComic;

public partial class EComicConfigurationWindow : Window
{
    private readonly EComicConfigurationEditor editor;

    public EComicConfigurationWindow()
        : this(new EComicConfigurationEditor())
    {
    }

    public EComicConfigurationWindow(EComicConfigurationEditor editor)
    {
        this.editor = editor;
        InitializeComponent();

        foreach (var source in EComicConfigurationEditor.NameSources)
        {
            NameSourceComboBox.Items.Add(new ComboBoxItem
            {
                Content = EComicConfigurationEditor.DescribeNameSource(source)
            });
        }

        CollectionEnabledCheckBox.IsChecked = editor.CollectionEnabled;
        TagWritersCheckBox.IsChecked = editor.TagWriters;
        ExtractCoversCheckBox.IsChecked = editor.ExtractCovers;
        NameSourceComboBox.SelectedIndex = Math.Max(0, IndexOf(editor.NameSource));
        CoverSizeBox.Value = Math.Clamp(editor.CoverSize, EComicSettings.MinCoverSize, EComicSettings.MaxCoverSize);
        ExtensionsTextBox.Text = editor.Extensions;
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        editor.CollectionEnabled = CollectionEnabledCheckBox.IsChecked == true;
        editor.TagWriters = TagWritersCheckBox.IsChecked == true;
        editor.ExtractCovers = ExtractCoversCheckBox.IsChecked == true;
        editor.NameSource = NameSourceComboBox.SelectedIndex switch
        {
            1 => EComicNameSource.Series,
            2 => EComicNameSource.Title,
            _ => EComicNameSource.FileName
        };
        if (CoverSizeBox.Value is { } size)
        {
            editor.CoverSize = (int)size;
        }

        editor.Extensions = ExtensionsTextBox.Text ?? string.Empty;
        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private static int IndexOf(EComicNameSource source)
    {
        for (var index = 0; index < EComicConfigurationEditor.NameSources.Count; index++)
        {
            if (EComicConfigurationEditor.NameSources[index] == source)
            {
                return index;
            }
        }

        return 0;
    }
}
