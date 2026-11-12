using SkyCD.Presentation.Scanning;
using SkyCD.UI.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SkyCD.Presentation.ViewModels;

public partial class AddToListDialogViewModel : ObservableObject, IDisposable
{
    private readonly IMediaDriveProvider driveProvider;
    private readonly IDisposable localizationSubscription;
    private bool isDisposed;

    public AddToListDialogViewModel(
        IMediaDriveProvider? driveProvider = null,
        AddToListSourceMode initialSourceMode = AddToListSourceMode.Media)
    {
        this.driveProvider = driveProvider ?? new SystemMediaDriveProvider();
        this.driveProvider.DrivesChanged += OnDrivesChanged;
        RefreshDrives();
        if (initialSourceMode != AddToListSourceMode.Media)
        {
            SourceMode = initialSourceMode;
        }

        RecomputeValidation();
        localizationSubscription = LocalizationObserver.Observe(this, static vm =>
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

    public bool IsSourceTitleOnly => SourceMode == AddToListSourceMode.TitleOnly;

    public bool IsScanOptionsEnabled => SourceMode != AddToListSourceMode.TitleOnly;

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
        var newDrives = driveProvider.GetSupportedDrives();

        if (AreDrivesEqual(AvailableDrives, newDrives))
        {
            return;
        }

        var previousSelected = SelectedDrive;

        for (var i = AvailableDrives.Count - 1; i >= 0; i--)
        {
            var existing = AvailableDrives[i];
            if (!newDrives.Any(d => string.Equals(d.Name, existing.Name, StringComparison.OrdinalIgnoreCase)))
            {
                AvailableDrives.RemoveAt(i);
            }
        }

        for (var i = 0; i < newDrives.Count; i++)
        {
            var target = newDrives[i];
            var existingIndex = -1;
            for (var j = 0; j < AvailableDrives.Count; j++)
            {
                if (string.Equals(AvailableDrives[j].Name, target.Name, StringComparison.OrdinalIgnoreCase))
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                if (i < AvailableDrives.Count)
                {
                    AvailableDrives.Insert(i, target);
                }
                else
                {
                    AvailableDrives.Add(target);
                }
            }
            else
            {
                if (AvailableDrives[existingIndex] != target)
                {
                    AvailableDrives[existingIndex] = target;
                }

                if (existingIndex != i)
                {
                    AvailableDrives.Move(existingIndex, i);
                }
            }
        }

        if (previousSelected is not null)
        {
            var matching = AvailableDrives.FirstOrDefault(d => string.Equals(d.Name, previousSelected.Name, StringComparison.OrdinalIgnoreCase));
            if (matching is not null)
            {
                SelectedDrive = matching;
            }
            else
            {
                SelectedDrive = AvailableDrives.Count > 0 ? AvailableDrives[0] : null;
            }
        }
        else
        {
            SelectedDrive = AvailableDrives.Count > 0 ? AvailableDrives[0] : null;
        }

        RecomputeValidation();
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        driveProvider.DrivesChanged -= OnDrivesChanged;
        localizationSubscription.Dispose();
    }

    private void OnDrivesChanged(object? sender, EventArgs e)
    {
        RefreshDrives();
    }

    private static bool AreDrivesEqual(IReadOnlyList<MediaDriveInfo> first, IReadOnlyList<MediaDriveInfo> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var i = 0; i < first.Count; i++)
        {
            if (first[i] != second[i])
            {
                return false;
            }
        }

        return true;
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
        OnPropertyChanged(nameof(IsSourceTitleOnly));
        OnPropertyChanged(nameof(IsScanOptionsEnabled));
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
            AddToListSourceMode.TitleOnly => string.IsNullOrWhiteSpace(MediaName)
                ? LocalizationService.Current.Get(TextKeys.MediaNameIsRequiredForTitleOnly)
                : null,
            _ => null
        };
    }
}