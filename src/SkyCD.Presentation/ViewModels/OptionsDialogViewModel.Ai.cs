using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    /// <summary>Index of the AI description category; it is appended after the always-present categories.</summary>
    private const int AiDescriptionCategoryIndex = 10;

    public ObservableCollection<AiScanExclusionItemViewModel> AiScanExclusions { get; } = [];

    [ObservableProperty] private AiScanExclusionItemViewModel? selectedAiScanExclusion;

    [ObservableProperty] private string aiDescriptionAlertMessage = string.Empty;

    [ObservableProperty] private int aiScanMaxFileSizeMegabytes = 1024;

    [ObservableProperty] private bool isAiDescriptionAvailable;

    public bool ShowAiDescriptionAlert => !string.IsNullOrWhiteSpace(AiDescriptionAlertMessage);

    public bool IsAiDescriptionCategorySelected => SelectedBuiltInIndex == AiDescriptionCategoryIndex;

    public bool ShowAiDescriptionSection =>
        IsAiDescriptionAvailable &&
        IsAiDescriptionCategorySelected &&
        IsCurrentCategoryVisibleInSearch;

    public event EventHandler? ResetAiScanExclusionsRequested;

    /// <summary>Shows or hides the AI description category; the settings apply only while its plugin is installed.</summary>
    public void SetAiDescriptionAvailable(bool available)
    {
        if (IsAiDescriptionAvailable == available)
        {
            return;
        }

        // The MCP tool page always follows the AI group, so only the AI-dependent pages trigger the fallback.
        var aiWasSelected = SelectedBuiltInIndex is AiDescriptionCategoryIndex or AiProviderCategoryIndex;
        IsAiDescriptionAvailable = available;
        if (!available && aiWasSelected)
        {
            RefreshCategoriesPreservingSelection(ToolbarCategoryId);
            return;
        }

        RefreshCategoriesPreservingSelection(SelectedSettingCategory?.Id);
        NotifySettingsVisibilityChanged();
    }

    [RelayCommand]
    private void AddAiScanExclusion()
    {
        var candidate = new AiScanExclusionItemViewModel
        {
            MimeType = LocalizationService.Current.Get(UiKeys.NewMimeType)
        };
        AiScanExclusions.Add(candidate);
        SelectedAiScanExclusion = candidate;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveAiScanExclusion))]
    private void RemoveAiScanExclusion()
    {
        if (SelectedAiScanExclusion is null)
        {
            return;
        }

        AiScanExclusions.Remove(SelectedAiScanExclusion);
        SelectedAiScanExclusion = AiScanExclusions.FirstOrDefault();
    }

    [RelayCommand]
    private void ResetAiScanExclusions()
    {
        ResetAiScanExclusionsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetAiScanExclusions(IEnumerable<AiScanExclusionDocument>? entries)
    {
        AiScanExclusions.Clear();
        foreach (var entry in (entries ?? [])
                     .Where(static entry => !string.IsNullOrWhiteSpace(entry.MimeType))
                     .Select(AiScanExclusionItemViewModel.FromDocument))
        {
            AiScanExclusions.Add(entry);
        }

        SelectedAiScanExclusion = AiScanExclusions.FirstOrDefault();
        AiDescriptionAlertMessage = string.Empty;
        RemoveAiScanExclusionCommand.NotifyCanExecuteChanged();
    }

    public IReadOnlyList<AiScanExclusionDocument> GetAiScanExclusions()
    {
        return AiScanExclusions
            .Select(static entry => entry.ToDocument())
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.MimeType))
            .ToArray();
    }

    partial void OnSelectedAiScanExclusionChanged(AiScanExclusionItemViewModel? value)
    {
        RemoveAiScanExclusionCommand.NotifyCanExecuteChanged();
    }

    partial void OnAiDescriptionAlertMessageChanged(string value)
    {
        OnPropertyChanged(nameof(ShowAiDescriptionAlert));
    }

    partial void OnAiScanMaxFileSizeMegabytesChanged(int value)
    {
        if (value < 1)
        {
            AiScanMaxFileSizeMegabytes = 1;
        }
    }

    partial void OnIsAiDescriptionAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(SettingCategories));
    }

    private bool CanRemoveAiScanExclusion()
    {
        return SelectedAiScanExclusion is not null;
    }
}
