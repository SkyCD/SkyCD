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

    public ObservableCollection<string> ScanConcurrencyModeOptions { get; } = [];

    [ObservableProperty] private ScanExclusionRuleItemViewModel? selectedScanExclusionRule;

    [ObservableProperty] private string scanAlertMessage = string.Empty;

    [ObservableProperty] private bool skipSymbolicLinks;

    [ObservableProperty] private int selectedHiddenSystemPolicyIndex;

    [ObservableProperty] private bool skipRecycleBins = true;

    [ObservableProperty] private bool skipWellKnownSystemFolders = true;

    [ObservableProperty] private bool skipEmptyFolders;

    [ObservableProperty] private bool skipOnlineOnlyCloudFiles;

    [ObservableProperty] private bool includeExtendedInfo;

    [ObservableProperty] private bool showScanErrorsAfterScan = true;

    [ObservableProperty] private bool saveScanErrorsToComments;

    [ObservableProperty] private bool ejectMediaAfterScan;

    [ObservableProperty] private bool activateMainWindowAfterScan;

    [ObservableProperty] private bool playSoundAfterScan;

    [ObservableProperty] private int selectedScanConcurrencyModeIndex;

    [ObservableProperty] private decimal scanConcurrencyManualWorkers = 2;

    [ObservableProperty] private decimal scanConcurrencyCpuSharePercent = 80;

    [ObservableProperty] private decimal scanConcurrencyLoadShrinkPercent = 70;

    [ObservableProperty] private decimal scanConcurrencyLoadSuspendPercent = 85;

    [ObservableProperty] private bool isMediaEjectionSupported = true;

    /// <summary>True when the worker count is an explicit user value instead of automatic.</summary>
    public bool IsManualScanConcurrency => SelectedScanConcurrencyModeIndex == 1;

    /// <summary>Explains where media ejection applies, or why the option is unavailable on this platform.</summary>
    public string MediaEjectionSupportHint => LocalizationService.Current.Get(
        IsMediaEjectionSupported ? UiKeys.MediaEjectionSupported : UiKeys.MediaEjectionUnsupported);

    /// <summary>Disables the eject option where the platform cannot eject media; the persisted value stays untouched.</summary>
    public void SetMediaEjectionSupported(bool supported) => IsMediaEjectionSupported = supported;

    public bool ShowScanAlert => !string.IsNullOrWhiteSpace(ScanAlertMessage);

    public bool IsOnlineOnlyCloudFilesOptionVisible => OperatingSystem.IsWindows();

    /// <summary>Existing scan category. Index 6 stays so legacy Options tab restoration still opens scan settings.</summary>
    public const string ScanCategoryId = "scan";

    public const int ScanCategoryIndex = 6;

    /// <summary>Fresh indexes so the pinned built-in sequence of the other pages stays unchanged.</summary>
    public const string ScanRulesCategoryId = "scan-rules";

    public const int ScanRulesCategoryIndex = 14;

    public const string ScanCompletionCategoryId = "scan-completion";

    public const int ScanCompletionCategoryIndex = 15;

    public bool IsScanCategorySelected => SelectedBuiltInIndex == ScanCategoryIndex;

    public bool ShowScanSection => IsScanCategorySelected && IsCurrentCategoryVisibleInSearch;

    public bool IsScanRulesCategorySelected => SelectedBuiltInIndex == ScanRulesCategoryIndex;

    public bool ShowScanRulesSection => IsScanRulesCategorySelected && IsCurrentCategoryVisibleInSearch;

    public bool IsScanCompletionCategorySelected => SelectedBuiltInIndex == ScanCompletionCategoryIndex;

    public bool ShowScanCompletionSection => IsScanCompletionCategorySelected && IsCurrentCategoryVisibleInSearch;

    /// <summary>The blank-pattern alert lives on the rules page, so Save reveals that page.</summary>
    public void RevealScanRulesPage()
    {
        if (!FilteredSettingCategories.Any(category => category.Id == ScanRulesCategoryId))
        {
            SettingsSearchText = string.Empty;
        }

        SelectedSettingCategory = SettingCategories.First(category => category.Id == ScanRulesCategoryId);
    }

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

    public void SetScanOptions(ScanSettingsSnapshot settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SkipSymbolicLinks = settings.SkipSymbolicLinks;
        SelectedHiddenSystemPolicyIndex =
            (int)ScanSettingsApplicator.ParseHiddenSystemFilePolicy(settings.HiddenSystemFilePolicy);
        SkipRecycleBins = settings.SkipRecycleBins;
        SkipWellKnownSystemFolders = settings.SkipWellKnownSystemFolders;
        SkipEmptyFolders = settings.SkipEmptyFolders;
        SkipOnlineOnlyCloudFiles = settings.SkipOnlineOnlyCloudFiles;
        IncludeExtendedInfo = settings.IncludeExtendedInfo;
        ShowScanErrorsAfterScan = settings.ShowScanErrorsAfterScan;
        SaveScanErrorsToComments = settings.SaveScanErrorsToComments;
        EjectMediaAfterScan = settings.EjectMediaAfterScan;
        ActivateMainWindowAfterScan = settings.ActivateMainWindowAfterScan;
        PlaySoundAfterScan = settings.PlaySoundAfterScan;
        SelectedScanConcurrencyModeIndex =
            ScanSettingsApplicator.ParseConcurrencyMode(settings.ScanConcurrencyMode) == ScanConcurrencyMode.Manual ? 1 : 0;
        ScanConcurrencyManualWorkers = Math.Clamp(settings.ScanConcurrencyManualWorkers, 1, ScanTraversalOptions.AbsoluteMaxConcurrency);
        ScanConcurrencyCpuSharePercent = Math.Clamp(settings.ScanConcurrencyCpuSharePercent, 1, 100);
        ScanConcurrencyLoadShrinkPercent = Math.Clamp(settings.ScanConcurrencyLoadShrinkPercent, 1, 99);
        ScanConcurrencyLoadSuspendPercent = Math.Clamp(settings.ScanConcurrencyLoadSuspendPercent, 2, 100);
    }

    public ScanSettingsSnapshot GetScanOptions()
    {
        var workers = (int)Math.Round(ScanConcurrencyManualWorkers);
        var cpuShare = (int)Math.Round(ScanConcurrencyCpuSharePercent);
        var shrink = Math.Clamp((int)Math.Round(ScanConcurrencyLoadShrinkPercent), 1, 99);
        var suspend = Math.Clamp((int)Math.Round(ScanConcurrencyLoadSuspendPercent), shrink + 1, 100);

        return new ScanSettingsSnapshot(
            SkipSymbolicLinks,
            ScanSettingsApplicator.ToHiddenSystemFilePolicyString(
                (HiddenSystemFilePolicy)Math.Clamp(SelectedHiddenSystemPolicyIndex, 0, 3)),
            SkipRecycleBins,
            SkipWellKnownSystemFolders,
            SkipEmptyFolders,
            SkipOnlineOnlyCloudFiles,
            IncludeExtendedInfo,
            ShowScanErrorsAfterScan,
            SaveScanErrorsToComments,
            EjectMediaAfterScan,
            ActivateMainWindowAfterScan,
            PlaySoundAfterScan,
            ScanSettingsApplicator.ToConcurrencyModeString(
                SelectedScanConcurrencyModeIndex == 1 ? ScanConcurrencyMode.Manual : ScanConcurrencyMode.Automatic),
            Math.Clamp(workers, 1, ScanTraversalOptions.AbsoluteMaxConcurrency),
            Math.Clamp(cpuShare, 1, 100),
            shrink,
            suspend);
    }

    partial void OnIsMediaEjectionSupportedChanged(bool value)
    {
        OnPropertyChanged(nameof(MediaEjectionSupportHint));
    }

    partial void OnSelectedScanExclusionRuleChanged(ScanExclusionRuleItemViewModel? value)
    {
        RemoveScanExclusionRuleCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedScanConcurrencyModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsManualScanConcurrency));
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
        ScanConcurrencyModeOptions.Clear();
        ScanConcurrencyModeOptions.Add(LocalizationService.Current.Get(UiKeys.ScanConcurrencyAutomatic));
        ScanConcurrencyModeOptions.Add(LocalizationService.Current.Get(UiKeys.ScanConcurrencyManual));
        OnPropertyChanged(nameof(MediaEjectionSupportHint));
    }
}
