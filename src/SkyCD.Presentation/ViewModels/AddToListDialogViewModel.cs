using SkyCD.Documents;
using SkyCD.Presentation.Accounts;
using SkyCD.Presentation.Scanning;
using SkyCD.SDK.Capabilities.Accounts;
using SkyCD.SDK.Capabilities.Sources;
using SkyCD.UI.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SkyCD.Presentation.ViewModels;

public partial class AddToListDialogViewModel : ObservableObject, IDisposable
{
    private readonly IMediaDriveProvider driveProvider;
    private readonly IReadOnlyList<IAccountSession> accountSessions;
    private readonly IDisposable localizationSubscription;
    private bool isDisposed;

    public AddToListDialogViewModel(
        IMediaDriveProvider? driveProvider = null,
        AddToListSourceMode initialSourceMode = AddToListSourceMode.Media,
        IReadOnlyList<LocationItemViewModel>? locationOptions = null,
        IReadOnlyList<IAccountSession>? accountSessions = null,
        IReadOnlyList<ISourceFactoryPluginCapability>? captureProviders = null)
    {
        this.driveProvider = driveProvider ?? new SystemMediaDriveProvider();
        this.accountSessions = accountSessions ?? [];
        this.driveProvider.DrivesChanged += OnDrivesChanged;
        foreach (var option in locationOptions ?? [])
        {
            LocationChoices.Add(option);
        }

        foreach (var provider in captureProviders ?? [])
        {
            CaptureProviders.Add(provider);
        }

        RefreshLocationChoiceLabels();
        SelectedLocation = LocationChoices[0];
        RefreshDrives();
        SourceMode = initialSourceMode == AddToListSourceMode.Media && !CanUseMedia
            ? AddToListSourceMode.Folder
            : initialSourceMode;
        SelectedCaptureProvider = CaptureProviders.FirstOrDefault();

        InternetCredentials.ConfirmCommand.CanExecuteChanged += OnInternetCredentialsChanged;
        InternetCredentials.PropertyChanged += OnInternetCredentialPropertyChanged;
        RecomputeValidation();
        localizationSubscription = LocalizationObserver.Observe(this, static vm =>
        {
            vm.RecomputeValidation();
            vm.RefreshLocationChoiceLabels();
            vm.OnPropertyChanged(nameof(vm.TargetParentDescription));
            vm.OnPropertyChanged(nameof(vm.HasTargetParentDescription));
        });
    }

    public ObservableCollection<MediaDriveInfo> AvailableDrives { get; } = [];

    /// <summary>Location picker choices; the first entry is the empty "(Unassigned)" choice.</summary>
    public ObservableCollection<LocationItemViewModel> LocationChoices { get; } =
    [
        new(string.Empty, string.Empty)
    ];

    [ObservableProperty]
    private LocationItemViewModel? selectedLocation;

    /// <summary>
    /// The picked location id, or null when "(Unassigned)" is selected. The picker binds through
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

    public bool CanConfirm =>
        string.IsNullOrWhiteSpace(ValidationMessage) && (!IsSourceFromAccount || SelectedAccount is { Status.IsConnected: true });

    /// <summary>Every discovered connector shown inline so accounts are managed without leaving the dialog.</summary>
    public ObservableCollection<AccountConnectorItemViewModel> AvailableAccountConnectors { get; } = [];

    /// <summary>True when the dialog was given the open catalog's account folders.</summary>
    public bool HasCatalogAccountFilter => catalogAccountFolders is not null;

    /// <summary>True when the picked account folder exists but its token is missing.</summary>
    public bool SelectedAccountNeedsSignIn => SelectedAccount is { Status.IsConnected: false };

    private IReadOnlyList<CatalogDocument>? catalogAccountFolders;

    public bool IsSourceMedia => SourceMode == AddToListSourceMode.Media;

    public bool IsSourceFolder => SourceMode == AddToListSourceMode.Folder;

    public bool IsSourceInternet => SourceMode == AddToListSourceMode.Internet;

    public bool IsSourceTitleOnly => SourceMode == AddToListSourceMode.TitleOnly;

    public bool IsSourceFromAccount => SourceMode == AddToListSourceMode.FromAccount;

    public bool IsSourceCapture => SourceMode == AddToListSourceMode.CaptureSource;

    /// <summary>Plugin-provided capture source types; the button only appears while at least one is enabled.</summary>
    public ObservableCollection<ISourceFactoryPluginCapability> CaptureProviders { get; } = [];

    [ObservableProperty] private ISourceFactoryPluginCapability? selectedCaptureProvider;

    public ObservableCollection<SourceKind> CaptureKinds { get; } = [];

    [ObservableProperty] private SourceKind? selectedCaptureKind;

    public ObservableCollection<SourceDevice> CaptureDevices { get; } = [];

    [ObservableProperty] private SourceDevice? selectedCaptureDevice;

    [ObservableProperty] private string captureLocation = string.Empty;

    [ObservableProperty] private string captureDestination = string.Empty;

    [ObservableProperty] private string captureLabel = string.Empty;

    /// <summary>Inline feedback for device enumeration and preview failures, never a modal dialog.</summary>
    [ObservableProperty] private string? captureMessage;

    [ObservableProperty] private bool isCapturePreviewRunning;

    public bool HasCaptureProviders => CaptureProviders.Count > 0;

    public bool IsCaptureProviderPickerVisible => IsSourceCapture && CaptureProviders.Count > 1;

    public bool ShowCaptureDevicePicker => IsSourceCapture && SelectedCaptureKind?.UseDevicePicker == true;

    public bool ShowCaptureFilePicker => IsSourceCapture && SelectedCaptureKind?.UseFilePicker == true;

    public bool ShowCaptureLocationField =>
        IsSourceCapture && SelectedCaptureKind is { UseDevicePicker: false, UseFilePicker: false };

    public string CaptureLocationLabel => SelectedCaptureKind?.LocationLabel ?? string.Empty;

    public ObservableCollection<AddToListAccountChoice> AccountChoices { get; } = [];

    [ObservableProperty] private AddToListAccountChoice? selectedAccount;

    public bool HasConnectedAccounts => AccountChoices.Any(choice => choice.Status.IsConnected);

    /// <summary>"From account" with no account folders explains itself and hosts the inline add form.</summary>
    public bool ShowAccountEmptyState => IsSourceFromAccount && AccountChoices.Count == 0;

    public bool ShowAccountPicker => IsSourceFromAccount && AccountChoices.Count > 0;

    public bool ShowAccountNoProducts => ShowAccountPicker && SelectedAccount is { Products.Count: 0 };

    /// <summary>The account folder names itself, so no media name is entered.</summary>
    public bool IsNameEntryVisible => !IsSourceFromAccount && !IsSourceCapture;

    /// <summary>An account folder always lands under the chosen destination.</summary>
    public bool IsDestinationSectionVisible => !IsSourceFromAccount && !IsSourceCapture;

    public bool CanUseMedia => AvailableDrives.Count > 0;

    /// <summary>True when at least one storage location is defined beyond the empty choice.</summary>
    public bool HasDefinedLocations => LocationChoices.Count > 1;

    /// <summary>
    /// The location picker applies to media roots created from a drive or a title-only entry, and only
    /// when a storage location actually exists to pick; otherwise the picker stays hidden entirely.
    /// </summary>
    public bool IsLocationPickerVisible =>
        SourceMode is AddToListSourceMode.Media or AddToListSourceMode.TitleOnly && HasDefinedLocations;

    /// <summary>
    /// Folder and Internet entries keep the scanned source path, so no separate location is offered;
    /// the dialog explains that where the picker is absent. The note only appears when locations
    /// exist at all: with no defined locations the picker is hidden everywhere and there is no
    /// asymmetry to explain, which also keeps the fixed dialog height the layout gates pin.
    /// </summary>
    public bool IsScannedPathNoteVisible =>
        HasDefinedLocations && SourceMode is AddToListSourceMode.Folder or AddToListSourceMode.Internet;

    public bool IsTargetSelectedFolder => TargetPlacement == AddToListTargetPlacement.SelectedFolder;

    public bool IsTargetNewMedia => TargetPlacement == AddToListTargetPlacement.NewMedia;

    /// <summary>
    /// Catalog container chosen before the dialog opened (a drop target or the tree selection).
    /// Captured up front so the destination cannot drift while the dialog is open.
    /// </summary>
    public string? TargetParentId { get; private set; }

    [ObservableProperty] private string? targetParentName;

    public string? TargetParentDescription => string.IsNullOrWhiteSpace(TargetParentName)
        ? null
        : LocalizationService.Current.Format(UiKeys.DropTargetFormat, TargetParentName);

    public bool HasTargetParentDescription =>
        IsTargetSelectedFolder && !string.IsNullOrWhiteSpace(TargetParentDescription);

    public void SetTargetParent(string? parentId, string? displayName)
    {
        TargetParentId = string.IsNullOrWhiteSpace(parentId) ? DropDestination.LibraryRootId : parentId;
        TargetParentName = displayName;
    }

    partial void OnTargetParentNameChanged(string? value)
    {
        OnPropertyChanged(nameof(TargetParentDescription));
        OnPropertyChanged(nameof(HasTargetParentDescription));
    }

    [ObservableProperty] private AddToListSourceMode sourceMode = AddToListSourceMode.Media;

    [ObservableProperty] private AddToListTargetPlacement targetPlacement = AddToListTargetPlacement.SelectedFolder;

    [ObservableProperty] private bool includeMediaInfo = true;

    [ObservableProperty] private bool includeSubfolders = true;

    [ObservableProperty] private bool includeFiles = true;

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

    /// <summary>
    /// <summary>
    /// Gives the dialog the open catalog's documents. The account list is derived from the account
    /// folders in this catalog, so a different catalog starts empty even when the machine holds
    /// connected sessions elsewhere. Null clears the filter (legacy machine-wide behavior).
    /// </summary>
    public void SetCatalogAccountFolders(IReadOnlyList<CatalogDocument>? catalogDocuments)
    {
        catalogAccountFolders = catalogDocuments;
        OnPropertyChanged(nameof(HasCatalogAccountFilter));
    }

    /// <summary>
    /// Reads the token-free snapshots of every discovered connector; only signed-in accounts become
    /// choices, so nothing connected shows the empty state instead of an unusable picker.
    /// When catalog folders were provided, choices are scoped to this catalog: folders already in
    /// the catalog appear (connected ones with live products, disconnected ones as sign-in
    /// required), and a catalog without folders shows the empty inline add form.
    /// </summary>
    public async Task RefreshAccountsAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAccountChoicesAsync(cancellationToken);
        await RefreshAvailableConnectorsAsync(cancellationToken);
    }

    private async Task RefreshAccountChoicesAsync(CancellationToken cancellationToken)
    {
        if (catalogAccountFolders is not null)
        {
            await RefreshAccountsFromCatalogAsync(catalogAccountFolders, cancellationToken);
            return;
        }

        var choices = new List<AddToListAccountChoice>();
        foreach (var session in accountSessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var status = await session.GetStatusAsync(cancellationToken);
                if (status.IsConnected)
                {
                    choices.Add(new AddToListAccountChoice(session, status, AccountCatalogBuilder.BuildPlan(status)));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A broken connector snapshot must not block the other accounts or the dialog.
            }
        }

        ApplyAccountChoices(choices);
    }

    private async Task RefreshAccountsFromCatalogAsync(
        IReadOnlyList<CatalogDocument> catalogDocuments,
        CancellationToken cancellationToken)
    {
        var folders = CatalogAccountReader.ReadAccountFolders(catalogDocuments);
        var liveByProvider = new Dictionary<string, (IAccountSession Session, AccountSessionStatus Status)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var session in accountSessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var status = await session.GetStatusAsync(cancellationToken);
                if (!liveByProvider.ContainsKey(status.ProviderId))
                {
                    liveByProvider[status.ProviderId] = (session, status);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A broken connector snapshot must not block the other accounts or the dialog.
            }
        }

        var choices = new List<AddToListAccountChoice>();
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (liveByProvider.TryGetValue(folder.ProviderId, out var live) &&
                string.Equals(live.Status.AccountId, folder.AccountKey, StringComparison.OrdinalIgnoreCase) &&
                live.Status.IsConnected)
            {
                choices.Add(new AddToListAccountChoice(live.Session, live.Status, AccountCatalogBuilder.BuildPlan(live.Status)));
            }
            else
            {
                if (!liveByProvider.TryGetValue(folder.ProviderId, out var match))
                {
                    continue;
                }

                var disconnected = new AccountSessionStatus
                {
                    ProviderId = folder.ProviderId,
                    DisplayName = string.IsNullOrWhiteSpace(folder.DisplayName) ? folder.ProviderId : folder.DisplayName,
                    AccountId = folder.AccountKey,
                    AccountEmail = folder.AccountEmail,
                    IsConnected = false,
                    LastSyncedUtc = folder.LastSyncedUtc,
                    Products = [],
                };
                choices.Add(new AddToListAccountChoice(
                    match.Session, disconnected, AccountCatalogBuilder.BuildPlan(disconnected)));
            }
        }

        ApplyAccountChoices(choices);
    }

    private void ApplyAccountChoices(List<AddToListAccountChoice> choices)
    {
        var previousFolderId = SelectedAccount?.Plan.FolderId;
        AccountChoices.Clear();
        foreach (var choice in choices.OrderBy(choice => choice.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            AccountChoices.Add(choice);
        }

        SelectedAccount = AccountChoices.FirstOrDefault(choice => choice.Plan.FolderId == previousFolderId)
                          ?? AccountChoices.FirstOrDefault(choice => choice.Status.IsConnected)
                          ?? AccountChoices.FirstOrDefault();
        OnPropertyChanged(nameof(HasConnectedAccounts));
        OnPropertyChanged(nameof(ShowAccountEmptyState));
        OnPropertyChanged(nameof(ShowAccountPicker));
        OnPropertyChanged(nameof(SelectedAccountNeedsSignIn));
        RecomputeValidation();
    }

    /// <summary>Refreshes the inline account connectors (all discovered sessions).</summary>
    public async Task RefreshAvailableConnectorsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var connector in AvailableAccountConnectors)
        {
            connector.StatusChanged -= OnConnectorStatusChanged;
        }

        AvailableAccountConnectors.Clear();
        foreach (var session in accountSessions)
        {
            var item = new AccountConnectorItemViewModel(session);
            item.StatusChanged += OnConnectorStatusChanged;
            AvailableAccountConnectors.Add(item);
            try
            {
                await item.RefreshAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // A broken connector must not block the inline form.
            }
        }

        OnPropertyChanged(nameof(AvailableAccountConnectors));
    }

    /// <summary>A connector signed in, synced or signed out, so the picker follows its new state.</summary>
    private void OnConnectorStatusChanged(object? sender, EventArgs e) => _ = RefreshAccountChoicesAfterConnectorChangeAsync();

    private async Task RefreshAccountChoicesAfterConnectorChangeAsync()
    {
        try
        {
            await RefreshAccountChoicesAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A refresh failure must not surface from a connector command that already succeeded.
        }
    }

    public ObservableCollection<DroppedSource> DroppedRoots { get; } = [];

    public bool HasMultipleDroppedRoots => DroppedRoots.Count > 1;

    [ObservableProperty] private DroppedSource? selectedDroppedRoot;

    /// <summary>Prefills the dialog from OS drag-and-drop sources; several roots stay pickable.</summary>
    public void ApplyDroppedSources(IReadOnlyList<DroppedSource> sources)
    {
        DroppedRoots.Clear();
        foreach (var source in sources)
        {
            DroppedRoots.Add(source);
        }

        OnPropertyChanged(nameof(HasMultipleDroppedRoots));

        SelectedDroppedRoot = null;
        if (DroppedRoots.Count > 0)
        {
            SelectedDroppedRoot = DroppedRoots[0];
        }
    }

    partial void OnSelectedDroppedRootChanged(DroppedSource? value)
    {
        if (value is not null)
        {
            ApplyDroppedSource(value);
        }
    }

    private void ApplyDroppedSource(DroppedSource source)
    {
        switch (source.Mode)
        {
            case AddToListSourceMode.Media when FindDrive(source.Value) is { } drive:
                SourceMode = AddToListSourceMode.Media;
                SelectedDrive = drive;
                MediaName = string.IsNullOrWhiteSpace(source.MediaName)
                    ? (string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.DisplayName : drive.VolumeLabel)
                    : source.MediaName!;
                break;
            case AddToListSourceMode.Internet:
                SourceMode = AddToListSourceMode.Internet;
                if (FillInternetFromUrl(source.Value) is not null &&
                    ToWebDavAddress(source.Value) is { } webDavAddress)
                {
                    // SkyCD reaches HTTP servers through WebDAV, the only HTTP transport it supports.
                    FillInternetFromUrl(webDavAddress);
                }

                break;
            default:
                SourceMode = AddToListSourceMode.Folder;
                SourceValue = source.Value;
                MediaName = source.MediaName ?? string.Empty;
                break;
        }
    }

    private static string? ToWebDavAddress(string address)
    {
        if (address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "webdavs://" + address[8..];
        }

        return address.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? "webdav://" + address[7..]
            : null;
    }

    private MediaDriveInfo? FindDrive(string path)
    {
        static string Trim(string value) => value.Trim().TrimEnd('/', '\\');

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var candidate = Trim(path);
        return AvailableDrives.FirstOrDefault(drive =>
            string.Equals(candidate, Trim(drive.RootDirectory), comparison) ||
            string.Equals(candidate, Trim(drive.Name), comparison));
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
        foreach (var connector in AvailableAccountConnectors)
        {
            connector.StatusChanged -= OnConnectorStatusChanged;
        }

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

        if (mode == AddToListSourceMode.CaptureSource && !HasCaptureProviders)
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

    partial void OnSelectedAccountChanged(AddToListAccountChoice? value)
    {
        OnPropertyChanged(nameof(ShowAccountNoProducts));
        OnPropertyChanged(nameof(SelectedAccountNeedsSignIn));
        RecomputeValidation();
    }

    partial void OnSelectedCaptureProviderChanged(ISourceFactoryPluginCapability? value)
    {
        CaptureKinds.Clear();
        CaptureDevices.Clear();
        CaptureMessage = null;
        if (value is not null)
        {
            foreach (var kind in value.GetSourceKinds())
            {
                CaptureKinds.Add(kind);
            }
        }

        SelectedCaptureKind = CaptureKinds.FirstOrDefault();
        NotifyCaptureStateChanged();
    }

    partial void OnSelectedCaptureKindChanged(SourceKind? value)
    {
        CaptureDevices.Clear();
        CaptureLocation = string.Empty;
        CaptureMessage = null;
        if (value is { UseDevicePicker: true } && SelectedCaptureProvider is { } provider)
        {
            foreach (var device in provider.GetDevices(value.Id))
            {
                CaptureDevices.Add(device);
            }

            if (CaptureDevices.Count == 0)
            {
                CaptureMessage = LocalizationService.Current.Get(UiKeys.CaptureNoDevices);
            }
        }

        SelectedCaptureDevice = CaptureDevices.FirstOrDefault();
        NotifyCaptureStateChanged();
    }

    partial void OnSelectedCaptureDeviceChanged(SourceDevice? value) => RecomputeValidation();

    partial void OnCaptureLocationChanged(string value) => RecomputeValidation();

    partial void OnCaptureDestinationChanged(string value) => RecomputeValidation();

    private void NotifyCaptureStateChanged()
    {
        OnPropertyChanged(nameof(ShowCaptureDevicePicker));
        OnPropertyChanged(nameof(ShowCaptureFilePicker));
        OnPropertyChanged(nameof(ShowCaptureLocationField));
        OnPropertyChanged(nameof(CaptureLocationLabel));
        RecomputeValidation();
    }

    /// <summary>The chosen location for the current kind: a device id, a file path or a stream URL.</summary>
    public string ResolveCaptureLocation() => SelectedCaptureKind switch
    {
        { UseDevicePicker: true } => SelectedCaptureDevice?.Id ?? string.Empty,
        _ => CaptureLocation.Trim()
    };

    public SourceDraft CreateCaptureDraft() => new(
        SelectedCaptureKind?.Id ?? string.Empty,
        ResolveCaptureLocation(),
        CaptureDestination.Trim(),
        CatalogFolder: string.Empty,
        string.IsNullOrWhiteSpace(CaptureLabel)
            ? SelectedCaptureKind?.DisplayName ?? string.Empty
            : CaptureLabel.Trim());

    /// <summary>Captures one frame through the plugin so its detector overlay can be inspected inline.</summary>
    public async Task<SourcePreviewResult?> CapturePreviewAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedCaptureProvider is not { } provider || SelectedCaptureKind is not { } kind)
        {
            CaptureMessage = LocalizationService.Current.Get(UiKeys.CaptureUnavailable);
            return null;
        }

        IsCapturePreviewRunning = true;
        CaptureMessage = null;
        try
        {
            var result = await provider.CapturePreviewAsync(
                new SourceConfiguration(
                    string.Empty,
                    kind.Id,
                    ResolveCaptureLocation(),
                    CaptureDestination.Trim(),
                    CatalogFolder: string.Empty,
                    CaptureLabel),
                cancellationToken);
            if (!result.Succeeded)
            {
                CaptureMessage = result.Message ?? LocalizationService.Current.Get(UiKeys.CaptureUnavailable);
            }

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CaptureMessage = exception.Message;
            return null;
        }
        finally
        {
            IsCapturePreviewRunning = false;
        }
    }

    /// <summary>Persists the configured source through the selected plugin, which owns its capture loop.</summary>
    public Task<SourceConfiguration> AddCaptureSourceAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedCaptureProvider is not { } provider)
        {
            throw new InvalidOperationException(LocalizationService.Current.Get(UiKeys.CaptureUnavailable));
        }

        return provider.AddSourceAsync(CreateCaptureDraft(), cancellationToken);
    }

    partial void OnSourceModeChanged(AddToListSourceMode value)
    {
        if (value == AddToListSourceMode.Media && AvailableDrives.Count == 0)
        {
            RefreshDrives();
        }

        if (value == AddToListSourceMode.FromAccount)
        {
            // The account folder always lands under the chosen destination.
            TargetPlacement = AddToListTargetPlacement.SelectedFolder;
        }

        RecomputeValidation();
        OnPropertyChanged(nameof(IsSourceMedia));
        OnPropertyChanged(nameof(IsSourceFolder));
        OnPropertyChanged(nameof(IsSourceInternet));
        OnPropertyChanged(nameof(IsSourceTitleOnly));
        OnPropertyChanged(nameof(IsSourceFromAccount));
        OnPropertyChanged(nameof(IsSourceCapture));
        OnPropertyChanged(nameof(IsCaptureProviderPickerVisible));
        OnPropertyChanged(nameof(ShowCaptureDevicePicker));
        OnPropertyChanged(nameof(ShowCaptureFilePicker));
        OnPropertyChanged(nameof(ShowCaptureLocationField));
        OnPropertyChanged(nameof(IsNameEntryVisible));
        OnPropertyChanged(nameof(IsDestinationSectionVisible));
        OnPropertyChanged(nameof(ShowAccountEmptyState));
        OnPropertyChanged(nameof(ShowAccountPicker));
        OnPropertyChanged(nameof(IsLocationPickerVisible));
        OnPropertyChanged(nameof(IsScannedPathNoteVisible));
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
        OnPropertyChanged(nameof(TargetParentDescription));
        OnPropertyChanged(nameof(HasTargetParentDescription));
    }

    partial void OnMediaNameChanged(string value)
    {
        RecomputeValidation();
    }

    partial void OnSourceValueChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            folderPathWasEntered = true;
        }

        RecomputeValidation();
    }

    private enum ValidationTarget
    {
        MediaName,
        Drive,
        FolderPath,
        InternetAddress,
        InternetPath,
        InternetCredentials,
        Account,
        CaptureType,
        CaptureLocation,
        CaptureDestination,
    }

    private bool folderPathWasEntered;
    private (ValidationTarget Target, string Message)? validationFailure;

    /// <summary>Inline error under the media-name box (media, title only and "as new media").</summary>
    public string? MediaNameValidationMessage => MessageFor(ValidationTarget.MediaName);

    /// <summary>Inline error under the drive picker.</summary>
    public string? DriveValidationMessage => MessageFor(ValidationTarget.Drive);

    /// <summary>
    /// Inline error under the folder path. An empty path the user never filled stays silent because
    /// the disabled OK button already communicates it; once anything was entered, clearing shows it.
    /// </summary>
    public string? FolderPathValidationMessage =>
        folderPathWasEntered ? MessageFor(ValidationTarget.FolderPath) : null;

    /// <summary>Inline error under the host/port row.</summary>
    public string? InternetAddressValidationMessage => MessageFor(ValidationTarget.InternetAddress);

    /// <summary>Inline error under the share and folder path.</summary>
    public string? InternetPathValidationMessage => MessageFor(ValidationTarget.InternetPath);

    /// <summary>Inline error under the credentials form.</summary>
    public string? InternetCredentialsValidationMessage => MessageFor(ValidationTarget.InternetCredentials);

    /// <summary>Inline error under the account picker.</summary>
    public string? AccountValidationMessage => MessageFor(ValidationTarget.Account);

    /// <summary>Inline error under the capture source type picker.</summary>
    public string? CaptureTypeValidationMessage => MessageFor(ValidationTarget.CaptureType);

    /// <summary>Inline error under the capture location input.</summary>
    public string? CaptureLocationValidationMessage => MessageFor(ValidationTarget.CaptureLocation);

    /// <summary>Inline error under the capture destination input.</summary>
    public string? CaptureDestinationValidationMessage => MessageFor(ValidationTarget.CaptureDestination);

    private string? MessageFor(ValidationTarget target) =>
        validationFailure is { } failure && failure.Target == target ? failure.Message : null;

    private void RecomputeValidation()
    {
        var failure = FindFirstValidationFailure();
        ValidationMessage = failure?.Message;
        if (failure?.Target != validationFailure?.Target || failure?.Message != validationFailure?.Message)
        {
            validationFailure = failure;
            OnPropertyChanged(nameof(MediaNameValidationMessage));
            OnPropertyChanged(nameof(DriveValidationMessage));
            OnPropertyChanged(nameof(FolderPathValidationMessage));
            OnPropertyChanged(nameof(InternetAddressValidationMessage));
            OnPropertyChanged(nameof(InternetPathValidationMessage));
            OnPropertyChanged(nameof(InternetCredentialsValidationMessage));
            OnPropertyChanged(nameof(AccountValidationMessage));
            OnPropertyChanged(nameof(CaptureTypeValidationMessage));
            OnPropertyChanged(nameof(CaptureLocationValidationMessage));
            OnPropertyChanged(nameof(CaptureDestinationValidationMessage));
        }

        ConfirmCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// The first failing field of the current mode owns the single visible message, so a form with
    /// several problems never stacks errors; <see cref="ValidationMessage"/> still gates confirm.
    /// </summary>
    private (ValidationTarget Target, string Message)? FindFirstValidationFailure()
    {
        if (TargetPlacement == AddToListTargetPlacement.NewMedia &&
            string.IsNullOrWhiteSpace(MediaName))
        {
            return (ValidationTarget.MediaName,
                LocalizationService.Current.Get(TextKeys.MediaNameIsRequiredWhenAddingAsNewMedia));
        }

        return SourceMode switch
        {
            AddToListSourceMode.Media => string.IsNullOrWhiteSpace(MediaName)
                ? (ValidationTarget.MediaName,
                    LocalizationService.Current.Get(TextKeys.MediaNameIsRequiredForMediaSource))
                : SelectedDrive is null && AvailableDrives.Count > 0
                    ? (ValidationTarget.Drive,
                        LocalizationService.Current.Get(TextKeys.DriveMustBeSelected))
                    : null,
            AddToListSourceMode.Folder => string.IsNullOrWhiteSpace(SourceValue)
                ? (ValidationTarget.FolderPath,
                    LocalizationService.Current.Get(TextKeys.FolderPathIsRequiredForFolderSource))
                : null,
            AddToListSourceMode.Internet => GetInternetValidationFailure(),
            AddToListSourceMode.TitleOnly => string.IsNullOrWhiteSpace(MediaName)
                ? (ValidationTarget.MediaName,
                    LocalizationService.Current.Get(TextKeys.MediaNameIsRequiredForTitleOnly))
                : null,
            AddToListSourceMode.FromAccount => SelectedAccount switch
            {
                null when HasConnectedAccounts =>
                    (ValidationTarget.Account, LocalizationService.Current.Get(UiKeys.AccountSelectPrompt)),
                { Status.IsConnected: false } =>
                    (ValidationTarget.Account, LocalizationService.Current.Get(UiKeys.AccountNotSignedIn)),
                _ => null,
            },
            AddToListSourceMode.CaptureSource => GetCaptureValidationFailure(),
            _ => null,
        };
    }

    private (ValidationTarget Target, string Message)? GetCaptureValidationFailure()
    {
        if (SelectedCaptureProvider is null)
        {
            return (ValidationTarget.CaptureType, LocalizationService.Current.Get(UiKeys.CaptureUnavailable));
        }

        if (SelectedCaptureKind is null)
        {
            return (ValidationTarget.CaptureType, LocalizationService.Current.Get(TextKeys.CaptureSourceTypeRequired));
        }

        var destination = CaptureDestination.Trim();
        if (string.IsNullOrWhiteSpace(destination))
        {
            return (ValidationTarget.CaptureDestination,
                LocalizationService.Current.Get(TextKeys.CaptureDestinationIsRequired));
        }

        if (!Path.IsPathFullyQualified(destination))
        {
            return (ValidationTarget.CaptureDestination,
                LocalizationService.Current.Get(TextKeys.CaptureDestinationNotAbsolute));
        }

        var location = ResolveCaptureLocation();
        if (string.IsNullOrWhiteSpace(location))
        {
            return (ValidationTarget.CaptureLocation,
                LocalizationService.Current.Get(TextKeys.CaptureSourceLocationRequired));
        }

        if (SelectedCaptureKind is { UseDevicePicker: false, UseFilePicker: false } &&
            !Uri.TryCreate(location, UriKind.Absolute, out _))
        {
            return (ValidationTarget.CaptureLocation,
                LocalizationService.Current.Get(TextKeys.CaptureSourceUrlInvalid));
        }

        return null;
    }
}
