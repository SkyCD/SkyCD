using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SkyCD.Plugin.FileMetadata;

public partial class FileMetadataConfigurationWindow : Window
{
    private readonly FileMetadataConfigurationEditor editor;

    public FileMetadataConfigurationWindow()
        : this(new FileMetadataConfigurationEditor())
    {
    }

    public FileMetadataConfigurationWindow(FileMetadataConfigurationEditor editor)
    {
        this.editor = editor;
        InitializeComponent();

        CollectionEnabledCheckBox.IsChecked = editor.CollectionEnabled;
        KeywordsEnabledCheckBox.IsChecked = editor.KeywordsEnabled;
        SplitHierarchicalCheckBox.IsChecked = editor.SplitHierarchicalTags;
        CommentsEnabledCheckBox.IsChecked = editor.CommentsEnabled;
        RatingsEnabledCheckBox.IsChecked = editor.RatingsEnabled;
        ExtensionsTextBox.Text = editor.Extensions;
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        editor.CollectionEnabled = CollectionEnabledCheckBox.IsChecked == true;
        editor.KeywordsEnabled = KeywordsEnabledCheckBox.IsChecked == true;
        editor.SplitHierarchicalTags = SplitHierarchicalCheckBox.IsChecked == true;
        editor.CommentsEnabled = CommentsEnabledCheckBox.IsChecked == true;
        editor.RatingsEnabled = RatingsEnabledCheckBox.IsChecked == true;
        editor.Extensions = ExtensionsTextBox.Text ?? string.Empty;
        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
