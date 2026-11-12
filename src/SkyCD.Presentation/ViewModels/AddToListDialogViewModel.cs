using SkyCD.Presentation.Scanning;
using SkyCD.UI.Localization;
using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SkyCD.Presentation.ViewModels;

public partial class AddToListDialogViewModel : ObservableObject
{
    private readonly IMediaDriveProvider driveProvider;

    public AddToListDialogViewModel(IMediaDriveProvider? driveProvider = null)
    {
        this.driveProvider = driveProvider ?? new SystemMediaDriveProvider();
        RefreshDrives();
        RecomputeValidation();
        LocalizationObserver.Observe(this, static vm =>
        {
            vm.RecomputeValidation();
            vm.OnPropertyChanged(nameof(SourceValueLabel));
        });
    }

    public ObservableCollection<MediaDriveInfo> AvailableDrives { get; } = [];

    [ObservableProperty] private MediaDriveInfo? selectedDrive;

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

    public void RefreshDrives()
    {
        AvailableDrives.Clear();
        var drives = driveProvider.GetSupportedDrives();
        foreach (var drive in drives)
        {
            AvailableDrives.Add(drive);
        }

        if (SelectedDrive is null || !AvailableDrives.Contains(SelectedDrive))
        {
            SelectedDrive = AvailableDrives.Count > 0 ? AvailableDrives[0] : null;
        }
    }

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
        if (value == AddToListSourceMode.Media && AvailableDrives.Count == 0)
        {
            RefreshDrives();
        }

        RecomputeValidation();
        OnPropertyChanged(nameof(SourceValueLabel));
        OnPropertyChanged(nameof(IsSourceMedia));
        OnPropertyChanged(nameof(IsSourceFolder));
        OnPropertyChanged(nameof(IsSourceInternet));
    }

    partial void OnSelectedDriveChanged(MediaDriveInfo? value)
    {
        if (value is not null && string.IsNullOrWhiteSpace(MediaName))
        {
            MediaName = !string.IsNullOrWhiteSpace(value.VolumeLabel)
                ? value.VolumeLabel
                : value.DisplayName;
        }

        RecomputeValidation();
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
                : (SelectedDrive is null && AvailableDrives.Count > 0
                    ? "A drive must be selected."
                    : null),
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