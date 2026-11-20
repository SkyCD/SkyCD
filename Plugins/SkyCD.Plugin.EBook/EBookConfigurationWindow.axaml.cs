using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SkyCD.Plugin.EBook;

public partial class EBookConfigurationWindow : Window
{
    private readonly EBookConfigurationEditor editor;

    public EBookConfigurationWindow()
        : this(new EBookConfigurationEditor())
    {
    }

    public EBookConfigurationWindow(EBookConfigurationEditor editor)
    {
        this.editor = editor;
        InitializeComponent();

        foreach (var source in EBookConfigurationEditor.NameSources)
        {
            NameSourceComboBox.Items.Add(new ComboBoxItem
            {
                Content = EBookConfigurationEditor.DescribeNameSource(source)
            });
        }

        CollectionEnabledCheckBox.IsChecked = editor.CollectionEnabled;
        TagAuthorsCheckBox.IsChecked = editor.TagAuthors;
        ExtractCoversCheckBox.IsChecked = editor.ExtractCovers;
        NameSourceComboBox.SelectedIndex = Math.Max(0, IndexOf(editor.NameSource));
        CoverSizeBox.Value = Math.Clamp(editor.CoverSize, EBookSettings.MinCoverSize, EBookSettings.MaxCoverSize);
        ExtensionsTextBox.Text = editor.Extensions;
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        editor.CollectionEnabled = CollectionEnabledCheckBox.IsChecked == true;
        editor.TagAuthors = TagAuthorsCheckBox.IsChecked == true;
        editor.ExtractCovers = ExtractCoversCheckBox.IsChecked == true;
        editor.NameSource = NameSourceComboBox.SelectedIndex switch
        {
            1 => EBookNameSource.Author,
            2 => EBookNameSource.Title,
            _ => EBookNameSource.FileName
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

    private static int IndexOf(EBookNameSource source)
    {
        for (var index = 0; index < EBookConfigurationEditor.NameSources.Count; index++)
        {
            if (EBookConfigurationEditor.NameSources[index] == source)
            {
                return index;
            }
        }

        return 0;
    }
}
