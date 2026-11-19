using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.Presentation.Scanning;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    public ObservableCollection<ScanExclusionRuleItemViewModel> ScanExclusionRules { get; } = [];

    public ObservableCollection<string> HiddenSystemPolicyOptions { get; } = [];

    [ObservableProperty] private ScanExclusionRuleItemViewModel? selectedScanExclusionRule;

    [ObservableProperty] private string scanAlertMessage = string.Empty;

    [ObservableProperty] private bool skipSymbolicLinks;

    [ObservableProperty] private int selectedHiddenSystemPolicyIndex;

    [ObservableProperty] private bool skipRecycleBins = true;

    [ObservableProperty] private bool skipWellKnownSystemFolders = true;

    [ObservableProperty] private bool skipEmptyFolders;

    [ObservableProperty] private bool skipOnlineOnlyCloudFiles;

    public bool ShowScanAlert => !string.IsNullOrWhiteSpace(ScanAlertMessage);

    public bool IsOnlineOnlyCloudFilesOptionVisible => OperatingSystem.IsWindows();

    public bool IsScanCategorySelected => SelectedTabIndex == 6;

    public bool ShowScanSection => IsScanCategorySelected && IsCurrentCategoryVisibleInSearch;

    public event EventHandler? ResetScanExclusionRulesRequested;

    [RelayCommand]
    private void AddScanExclusionRule()
    {
        var candidate = new ScanExclusionRuleItemViewModel
        {
            Pattern = LocalizationService.Current.Get(UiKeys.NewRule),
            EntryTypeIndex = (int)ScanEntryType.Both
        };
        ScanExclusionRules.Add(candidate);
        SelectedScanExclusionRule = candidate;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveScanExclusionRule))]
    private void RemoveScanExclusionRule()
    {
        if (SelectedScanExclusionRule is null)
        {
            return;
        }

        ScanExclusionRules.Remove(SelectedScanExclusionRule);
        SelectedScanExclusionRule = ScanExclusionRules.FirstOrDefault();
    }

    [RelayCommand]
    private void ResetScanExclusionRules()
    {
        ResetScanExclusionRulesRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetScanExclusionRules(IEnumerable<ScanExclusionRuleDocument>? rules)
    {
        ScanExclusionRules.Clear();
        foreach (var rule in (rules ?? [])
                     .Where(static rule => !string.IsNullOrWhiteSpace(rule.Pattern))
                     .Select(ScanExclusionRuleItemViewModel.FromDocument))
        {
            ScanExclusionRules.Add(rule);
        }

        SelectedScanExclusionRule = ScanExclusionRules.FirstOrDefault();
        ScanAlertMessage = string.Empty;
        RemoveScanExclusionRuleCommand.NotifyCanExecuteChanged();
    }

    public IReadOnlyList<ScanExclusionRuleDocument> GetScanExclusionRules()
    {
        return ScanExclusionRules
            .Select(static rule => rule.ToDocument())
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.Pattern))
            .ToArray();
    }

    public void SetScanOptions(
        bool skipSymbolicLinks,
        string hiddenSystemFilePolicy,
        bool skipRecycleBins,
        bool skipWellKnownSystemFolders,
        bool skipEmptyFolders,
        bool skipOnlineOnlyCloudFiles)
    {
        SkipSymbolicLinks = skipSymbolicLinks;
        SelectedHiddenSystemPolicyIndex =
            (int)ScanSettingsApplicator.ParseHiddenSystemFilePolicy(hiddenSystemFilePolicy);
        SkipRecycleBins = skipRecycleBins;
        SkipWellKnownSystemFolders = skipWellKnownSystemFolders;
        SkipEmptyFolders = skipEmptyFolders;
        SkipOnlineOnlyCloudFiles = skipOnlineOnlyCloudFiles;
    }

    public (bool SkipSymbolicLinks, string HiddenSystemFilePolicy, bool SkipRecycleBins, bool SkipWellKnownSystemFolders, bool SkipEmptyFolders, bool SkipOnlineOnlyCloudFiles) GetScanOptions()
    {
        return (
            SkipSymbolicLinks,
            ScanSettingsApplicator.ToHiddenSystemFilePolicyString(
                (HiddenSystemFilePolicy)Math.Clamp(SelectedHiddenSystemPolicyIndex, 0, 3)),
            SkipRecycleBins,
            SkipWellKnownSystemFolders,
            SkipEmptyFolders,
            SkipOnlineOnlyCloudFiles);
    }

    partial void OnSelectedScanExclusionRuleChanged(ScanExclusionRuleItemViewModel? value)
    {
        RemoveScanExclusionRuleCommand.NotifyCanExecuteChanged();
    }

    partial void OnScanAlertMessageChanged(string value)
    {
        OnPropertyChanged(nameof(ShowScanAlert));
    }

    private bool CanRemoveScanExclusionRule()
    {
        return SelectedScanExclusionRule is not null;
    }

    private void RefreshScanLocalization()
    {
        HiddenSystemPolicyOptions.Clear();
        HiddenSystemPolicyOptions.Add(LocalizationService.Current.Get(UiKeys.HiddenSystemPolicyInclude));
        HiddenSystemPolicyOptions.Add(LocalizationService.Current.Get(UiKeys.HiddenSystemPolicySkipHidden));
        HiddenSystemPolicyOptions.Add(LocalizationService.Current.Get(UiKeys.HiddenSystemPolicySkipSystem));
        HiddenSystemPolicyOptions.Add(LocalizationService.Current.Get(UiKeys.HiddenSystemPolicySkipHiddenAndSystem));
    }
}
