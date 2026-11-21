using System;
using Avalonia.Controls;
using SkyCD.Plugin.EBook.Models;

namespace SkyCD.Plugin.EBook.Configuration;

public partial class EBookConfigurationPage : UserControl
{
    private readonly EBookConfigurationEditor editor;

    public EBookConfigurationPage()
        : this(new EBookConfigurationEditor())
    {
    }

    public EBookConfigurationPage(EBookConfigurationEditor editor)
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

    public void UpdateDraft()
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
