using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using SkyCD.Plugin.Feature.GenDesc.ViewModels;
using TextMateSharp.Grammars;

namespace SkyCD.Plugin.Feature.GenDesc.Views;

public partial class GenDescTemplatesView : UserControl
{
    private readonly TextEditor? editor;
    private TextMate.Installation? installation;
    private RegistryOptions? registryOptions;
    private GenDescTemplatesViewModel? observed;
    private bool grammarApplied;

    public GenDescTemplatesView()
    {
        InitializeComponent();
        editor = this.FindControl<TextEditor>("HighlightedEditor");
        AttachedToVisualTree += (_, _) => ApplyPreview();
        DataContextChanged += (_, _) => ObserveViewModel();
    }

    private void ObserveViewModel()
    {
        if (observed is not null)
        {
            observed.PropertyChanged -= OnViewModelChanged;
        }

        observed = DataContext as GenDescTemplatesViewModel;
        if (observed is not null)
        {
            observed.PropertyChanged += OnViewModelChanged;
        }

        SyncText();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(GenDescTemplatesViewModel.EditorBody), StringComparison.Ordinal))
        {
            SyncText();
        }
    }

    private void ApplyPreview()
    {
        if (editor is null)
        {
            return;
        }

        try
        {
            registryOptions ??= GenDescTemplateHighlighting.CreateRegistryOptions(IsDarkTheme());
            installation ??= editor.InstallTextMate(registryOptions);
            if (!grammarApplied)
            {
                installation.SetGrammarFile(GenDescTemplateHighlighting.EnsureGrammarFile());
                grammarApplied = true;
            }

            installation.SetTheme(registryOptions.LoadTheme(
                IsDarkTheme() ? ThemeName.DarkPlus : ThemeName.LightPlus));
        }
        catch (Exception)
        {
            // The preview is decorative: a highlighting failure must never break the editor.
            installation = null;
        }

        SyncText();
    }

    private void SyncText()
    {
        if (editor is null)
        {
            return;
        }

        var body = observed?.EditorBody ?? string.Empty;
        if (!string.Equals(editor.Document.Text, body, StringComparison.Ordinal))
        {
            editor.Document.Text = body;
        }
    }

    private bool IsDarkTheme() => ActualThemeVariant == ThemeVariant.Dark;
}
