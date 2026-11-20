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
        AddToListSourceMode initialSourceMode = AddToListSourceMode.Media,
        IReadOnlyList<LocationItemViewModel>? locationOptions = null)
    {
        this.driveProvider = driveProvider ?? new SystemMediaDriveProvider();
        this.driveProvider.DrivesChanged += OnDrivesChanged;
        foreach (var option in locationOptions ?? [])
        {
            LocationChoices.Add(option);
        }

        RefreshLocationChoiceLabels();
        SelectedLocation = LocationChoices[0];
        RefreshDrives();
        SourceMode = initialSourceMode == AddToListSourceMode.Media && !CanUseMedia
            ? AddToListSourceMode.Folder
            : initialSourceMode;

        InternetCredentials.ConfirmCommand.CanExecuteChanged += OnInternetCredentialsChanged;
        InternetCredentials.PropertyChanged += OnInternetCredentialPropertyChanged;
        RecomputeValidation();
        localizationSubscription = LocalizationObserver.Observe(this, static vm =>
        {
            vm.RecomputeValidation();
            vm.RefreshLocationChoiceLabels();
        });
    }

    public ObservableCollection<MediaDriveInfo> AvailableDrives { get; } = [];

    /// <summary>Location picker choices; the first entry is the empty "(none)" choice.</summary>
    public ObservableCollection<LocationItemViewModel> LocationChoices { get; } =
    [
        new(string.Empty, string.Empty)
    ];

    [ObservableProperty]
    private LocationItemViewModel? selectedLocation;

    /// <summary>
    /// The picked location id, or null when "(none)" is selected. The picker binds through
    /// <see cref="SelectedLocation"/>, which keeps this value in sync in both directions.
    /// </summary>
    public string? SelectedLocationId
    {
        get => string.IsNullOrEmpty(SelectedLocation?.LocationId) ? null : SelectedLocation!.LocationId;
        set
        {
            SelectedLocation = LocationChoices.FirstOrDefault(choice =>
                string.Equals(choice.LocationId, value ?? string.Empty, StringComparison.Ordinal))
                ?? LocationChoices[0];
        }
    }

    partial void OnSelectedLocationChanged(LocationItemViewModel? value) =>
        OnPropertyChanged(nameof(SelectedLocationId));

    private void RefreshLocationChoiceLabels()
    {
        LocationChoices[0].Name = LocalizationService.Current.Get(UiKeys.LocationNone);
    }

    [ObservableProperty] private MediaDriveInfo? selectedDrive;

    public bool CanConfirm => string.IsNullOrWhiteSpace(ValidationMessage);

    public bool IsSourceMedia => SourceMode == AddToListSourceMode.Media;

    public bool IsSourceFolder => SourceMode == AddToListSourceMode.Folder;

    public bool IsSourceInternet => SourceMode == AddToListSourceMode.Internet;

    public bool IsSourceTitleOnly => SourceMode == AddToListSourceMode.TitleOnly;

    public bool IsScanOptionsEnabled => SourceMode != AddToListSourceMode.TitleOnly;

    public bool CanUseMedia => AvailableDrives.Count > 0;

    /// <summary>The location picker applies to media roots created from a drive or a title-only entry.</summary>
    public bool IsLocationPickerVisible => SourceMode is AddToListSourceMode.Media or AddToListSourceMode.TitleOnly;

    public bool IsTargetSelectedFolder => TargetPlacement == AddToListTargetPlacement.SelectedFolder;

    public bool IsTargetNewMedia => TargetPlacement == AddToListTargetPlacement.NewMedia;

    [ObservableProperty] private AddToListSourceMode sourceMode = AddToListSourceMode.Media;

    [ObservableProperty] private AddToListTargetPlacement targetPlacement = AddToListTargetPlacement.SelectedFolder;

    [ObservableProperty] private bool includeMediaInfo = true;

    [ObservableProperty] private bool includeSubfolders = true;

    [ObservableProperty] private bool includeFiles = true;

    [ObservableProperty] private bool includeExtendedInfo;

    [ObservableProperty] private string mediaName = string.Empty;

    [ObservableProperty] private string sourceValue = string.Empty;

    [ObservableProperty] private bool dialogAccepted;

    [ObservableProperty] private string? validationMessage;

    public void RefreshDrives()
    {
        var couldUseMedia = CanUseMedia;
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

        if (couldUseMedia != CanUseMedia)
        {
            OnPropertyChanged(nameof(CanUseMedia));
        }

        if (!CanUseMedia && SourceMode == AddToListSourceMode.Media)
        {
            SourceMode = AddToListSourceMode.Folder;
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
        InternetCredentials.ConfirmCommand.CanExecuteChanged -= OnInternetCredentialsChanged;
        InternetCredentials.PropertyChanged -= OnInternetCredentialPropertyChanged;
        CheckInternetConnectionCommand.Cancel();
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
        if (!Enum.TryParse<AddToListSourceMode>(modeKey, true, out var mode))
        {
            return;
        }

        if (mode == AddToListSourceMode.Media && !CanUseMedia)
        {
            return;
        }

        SourceMode = mode;
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
        OnPropertyChanged(nameof(IsSourceMedia));
        OnPropertyChanged(nameof(IsSourceFolder));
        OnPropertyChanged(nameof(IsSourceInternet));
        OnPropertyChanged(nameof(IsSourceTitleOnly));
        OnPropertyChanged(nameof(IsScanOptionsEnabled));
        OnPropertyChanged(nameof(IsLocationPickerVisible));
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
            AddToListSourceMode.Internet => GetInternetValidationMessage(),
            AddToListSourceMode.TitleOnly => string.IsNullOrWhiteSpace(MediaName)
                ? LocalizationService.Current.Get(TextKeys.MediaNameIsRequiredForTitleOnly)
                : null,
            _ => null
        };
    }
}
