using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Documents;
using SkyCD.Presentation.Scanning;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class ScanExclusionRuleItemViewModel : ObservableObject
{
    [ObservableProperty] private string pattern = string.Empty;

    [ObservableProperty] private int entryTypeIndex = (int)ScanEntryType.Both;

    [ObservableProperty] private bool isInclude;

    public ObservableCollection<string> EntryTypeOptions { get; } = [];

    public ScanExclusionRuleItemViewModel()
    {
        RefreshLocalization();
        LocalizationObserver.Observe(this, static vm => vm.RefreshLocalization());
    }

    public ScanExclusionRuleDocument ToDocument()
    {
        return new ScanExclusionRuleDocument
        {
            Pattern = Pattern.Trim(),
            EntryType = ScanSettingsApplicator.ToEntryTypeString(
                (ScanEntryType)Math.Clamp(EntryTypeIndex, 0, 2)),
            IsInclude = IsInclude
        };
    }

    public static ScanExclusionRuleItemViewModel FromDocument(ScanExclusionRuleDocument document)
    {
        return new ScanExclusionRuleItemViewModel
        {
            Pattern = document.Pattern,
            EntryTypeIndex = (int)ScanSettingsApplicator.ParseEntryType(document.EntryType),
            IsInclude = document.IsInclude
        };
    }

    private void RefreshLocalization()
    {
        EntryTypeOptions.Clear();
        EntryTypeOptions.Add(LocalizationService.Current.Get(UiKeys.EntryTypeFile));
        EntryTypeOptions.Add(LocalizationService.Current.Get(UiKeys.EntryTypeFolder));
        EntryTypeOptions.Add(LocalizationService.Current.Get(UiKeys.EntryTypeBoth));
    }
}
