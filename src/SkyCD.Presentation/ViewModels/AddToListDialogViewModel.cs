using SkyCD.UI.Localization;
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SkyCD.Presentation.ViewModels;

public partial class AddToListDialogViewModel : ObservableObject
{
    public AddToListDialogViewModel()
    {
        RecomputeValidation();
        LocalizationObserver.Observe(this, static vm =>
        {
            vm.RecomputeValidation();
            vm.OnPropertyChanged(nameof(SourceValueLabel));
        });
    }

    public bool CanConfirm => string.IsNullOrWhiteSpace(ValidationMessage);

    public bool IsSourceMedia => SourceMode == AddToListSourceMode.Media;

    public bool IsSourceFolder => SourceMode == AddToListSourceMode.Folder;

    public bool IsSourceInternet => SourceMode == AddToListSourceMode.Internet;

    public bool IsTargetSelectedFolder => TargetPlacement == AddToListTargetPlacement.SelectedFolder;

    public bool IsTargetNewMedia => TargetPlacement == AddToListTargetPlacement.NewMedia;

    [ObservableProperty] private AddToListSourceMode sourceMode = AddToListSourceMode.Media;

    [ObservableProperty] private AddToListTargetPlacement targetPlacement = AddToListTargetPlacement.SelectedFolder;

    [ObservableProperty] private bool includeMediaInfo = true;

    [ObservableProperty] private bool includeSubfolders;

    [ObservableProperty] private bool includeExtendedInfo;

    [ObservableProperty] private string mediaName = string.Empty;

    [ObservableProperty] private string sourceValue = string.Empty;

    [ObservableProperty] private bool dialogAccepted;

    [ObservableProperty] private string? validationMessage;

    public string SourceValueLabel => SourceMode == AddToListSourceMode.Internet ? LocalizationService.Current.Get(TextKeys.Address) : LocalizationService.Current.Get(TextKeys.Folder);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        DialogAccepted = true;
    }

    [RelayCommand]
    private void SelectSource(string modeKey)
    {
        if (Enum.TryParse<AddToListSourceMode>(modeKey, true, out var mode))
        {
            SourceMode = mode;
        }
    }

    [RelayCommand]
    private void SelectTarget(string targetKey)
    {
        if (Enum.TryParse<AddToListTargetPlacement>(targetKey, true, out var target))
        {
            TargetPlacement = target;
        }
    }

    partial void OnSourceModeChanged(AddToListSourceMode value)
    {
        RecomputeValidation();
        OnPropertyChanged(nameof(SourceValueLabel));
        OnPropertyChanged(nameof(IsSourceMedia));
        OnPropertyChanged(nameof(IsSourceFolder));
        OnPropertyChanged(nameof(IsSourceInternet));
    }

    partial void OnTargetPlacementChanged(AddToListTargetPlacement value)
    {
        RecomputeValidation();
        OnPropertyChanged(nameof(IsTargetSelectedFolder));
        OnPropertyChanged(nameof(IsTargetNewMedia));
    }

    partial void OnMediaNameChanged(string value)
    {
        RecomputeValidation();
    }

    partial void OnSourceValueChanged(string value)
    {
        RecomputeValidation();
    }

    private void RecomputeValidation()
    {
        ValidationMessage = GetValidationMessage();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    private string? GetValidationMessage()
    {
        if (TargetPlacement == AddToListTargetPlacement.NewMedia &&
            string.IsNullOrWhiteSpace(MediaName))
        {
            return LocalizationService.Current.Get(TextKeys.MediaNameIsRequiredWhenAddingAsNewMedia);
        }

        return SourceMode switch
        {
            AddToListSourceMode.Media => string.IsNullOrWhiteSpace(MediaName)
                ? LocalizationService.Current.Get(TextKeys.MediaNameIsRequiredForMediaSource)
                : null,
            AddToListSourceMode.Folder => string.IsNullOrWhiteSpace(SourceValue)
                ? LocalizationService.Current.Get(TextKeys.FolderPathIsRequiredForFolderSource)
                : null,
            AddToListSourceMode.Internet => string.IsNullOrWhiteSpace(SourceValue)
                ? LocalizationService.Current.Get(TextKeys.AddressIsRequiredForInternetSource)
                : null,
            _ => null
        };
    }
}