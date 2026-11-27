using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Presentation.Scanning;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class AddToListDialogViewModel
{
    private static readonly RemoteCatalogProviderRegistry InternetProviders = RemoteCatalogProviderRegistry.Default;
    private static readonly IReadOnlyCollection<string> AvailableInternetProtocols = Array.AsReadOnly(
        InternetProviders.Schemes.Where(scheme => scheme != "smb" || OperatingSystem.IsWindows()).ToArray());
    private RemoteLocation? internetLocation;

    public IReadOnlyCollection<string> InternetProtocols => AvailableInternetProtocols;
    public LoginDialogViewModel InternetCredentials { get; } = new();

    [ObservableProperty] private string internetProtocol = "ftp";

    [ObservableProperty] private string internetHost = string.Empty;

    [ObservableProperty] private string internetPort = string.Empty;

    [ObservableProperty] private string? internetCheckStatus;

    private int internetSettingsRevision;

    [ObservableProperty] private string internetPath = string.Empty;

    public bool IsSmbInternetProtocol => InternetProtocol == "smb";

    public string SuggestedInternetPort => DefaultInternetPort.ToString(CultureInfo.InvariantCulture);

    private int DefaultInternetPort => InternetCredentials.IsFtp
        ? InternetCredentials.TlsMode == RemoteTlsMode.Implicit ? 990 : 21
        : InternetProviders.Get(InternetProtocol).DefaultPort;

    public RemoteScanOptions CreateInternetScanOptions(
        string? targetParentId,
        ScanTraversalOptions? traversal = null,
        bool includeExtendedInfo = false) =>
        new(InternetCredentials.EffectiveAddress,
            InternetCredentials.UseAnonymous ? null : InternetCredentials.Username,
            InternetCredentials.IsCredentialEntryEnabled && InternetCredentials.IsPasswordAuthentication
                ? InternetCredentials.Password
                : null,
            InternetCredentials.UseAnonymous,
            string.IsNullOrWhiteSpace(MediaName) ? null : MediaName,
            targetParentId,
            includeExtendedInfo,
            traversal: traversal,
            authMethod: InternetCredentials.AuthMethod,
            keyFilePath: InternetCredentials.IsKeyAuthentication ? InternetCredentials.KeyFilePath : null,
            keyFilePassphrase: InternetCredentials.IsKeyAuthentication ? InternetCredentials.KeyFilePassphrase : null,
            domain: IsSmbInternetProtocol ? InternetCredentials.Domain : null);

    public void ClearInternetSecrets()
    {
        InternetCredentials.Password = string.Empty;
        InternetCredentials.KeyFilePassphrase = string.Empty;
    }

    public string? FillInternetFromUrl(string? text)
    {
        var address = text?.Trim() ?? string.Empty;
        var schemeEnd = address.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0 || !Uri.CheckSchemeName(address[..schemeEnd]) ||
            address.Contains('#') || address.Any(char.IsControl))
        {
            return UiKeys.InvalidInternetClipboardUrl;
        }

        var authorityEnd = address.IndexOfAny(['/', '?'], schemeEnd + 3);
        var authorityUrl = authorityEnd < 0 ? address : address[..authorityEnd];
        if (!Uri.TryCreate(authorityUrl, UriKind.Absolute, out var endpoint) ||
            string.IsNullOrEmpty(endpoint.Host) || endpoint.Port == 0)
        {
            return UiKeys.InvalidInternetClipboardUrl;
        }

        RemoteLocation location;
        try
        {
            location = RemoteLocation.Parse(address, InternetProviders);
        }
        catch (RemoteSchemeNotSupportedException)
        {
            return UiKeys.UnsupportedInternetClipboardUrl;
        }
        catch (Exception ex) when (ex is RemoteConnectionException or UriFormatException)
        {
            return UiKeys.InvalidInternetClipboardUrl;
        }

        if (!InternetProtocols.Contains(location.Scheme))
        {
            return UiKeys.UnsupportedInternetClipboardUrl;
        }

        if (location.Scheme == "smb" && location.Path == "/")
        {
            return UiKeys.InternetShareRequired;
        }

        InternetProtocol = location.Scheme;
        InternetHost = location.Host;
        var authority = authorityUrl[(schemeEnd + 3)..];
        var hostAndPort = authority[(authority.LastIndexOf('@') + 1)..];
        InternetPort = hostAndPort.LastIndexOf(':') > hostAndPort.LastIndexOf(']')
            ? location.Port.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        InternetPath = location.Path;
        var hasCredentials = RemoteLocation.TryReadUserInfo(address, out var username, out var password);
        InternetCredentials.AuthMethodIndex = 0;
        InternetCredentials.KeyFilePath = string.Empty;
        InternetCredentials.KeyFilePassphrase = string.Empty;
        InternetCredentials.Domain = string.Empty;
        InternetCredentials.UseAnonymous = !hasCredentials && InternetCredentials.IsAnonymousAvailable;
        InternetCredentials.Username = username;
        InternetCredentials.Password = password;
        InternetCredentials.TlsModeIndex = (int)location.TlsMode;
        return null;
    }

    partial void OnInternetProtocolChanged(string value)
    {
        InternetCredentials.SetProtocol(value);
        OnPropertyChanged(nameof(SuggestedInternetPort));
        OnPropertyChanged(nameof(IsSmbInternetProtocol));
        UpdateInternetLocation();
    }

    partial void OnInternetHostChanged(string value) => UpdateInternetLocation();

    partial void OnInternetPortChanged(string value) => UpdateInternetLocation();

    partial void OnInternetPathChanged(string value) => UpdateInternetLocation();

    private void OnInternetCredentialsChanged(object? sender, EventArgs e) => UpdateInternetValidation();

    private void OnInternetCredentialPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginDialogViewModel.TlsMode))
        {
            OnPropertyChanged(nameof(SuggestedInternetPort));
            UpdateInternetLocation();
        }
    }

    private void UpdateInternetValidation()
    {
        internetSettingsRevision++;
        InternetCheckStatus = null;
        CheckInternetConnectionCommand.NotifyCanExecuteChanged();
        RecomputeValidation();
    }

    private bool CanCheckInternetConnection() =>
        InternetProtocol != "sftp" && GetInternetValidationFailure() is null;

    [RelayCommand(CanExecute = nameof(CanCheckInternetConnection))]
    private async Task CheckInternetConnectionAsync(CancellationToken cancellationToken)
    {
        if (!CanCheckInternetConnection())
        {
            return;
        }

        var revision = internetSettingsRevision;
        var options = CreateInternetScanOptions(null);
        InternetCheckStatus = LocalizationService.Current.Get(UiKeys.InternetChecking);
        string statusKey;
        try
        {
            await Task.Run(() =>
            {
                var location = RemoteLocation.Parse(options.Address, InternetProviders);
                var connector = InternetProviders.Get(location.Scheme);
                var credentials = new RemoteCredentials(options.Username, options.Password, options.Anonymous, domain: options.Domain);
                using var session = connector.Connect(location, credentials, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                session.ListDirectory(location.Path);
            }, cancellationToken);
            statusKey = UiKeys.InternetCheckSucceeded;
        }
        catch (RemoteAuthenticationException)
        {
            statusKey = UiKeys.InternetCheckAuthenticationFailed;
        }
        catch (Exception)
        {
            statusKey = UiKeys.InternetCheckFailed;
        }

        if (!cancellationToken.IsCancellationRequested && revision == internetSettingsRevision)
        {
            InternetCheckStatus = LocalizationService.Current.Get(statusKey);
        }
    }

    private void UpdateInternetLocation()
    {
        internetLocation = null;
        var host = InternetHost.Trim();
        if (Uri.CheckHostName(host) != UriHostNameType.Unknown && TryGetInternetPort(out var port))
        {
            var path = RemoteLocation.EscapePath(InternetPath.TrimStart('/'));
            try
            {
                var address = new UriBuilder(InternetProtocol, host, port, path).Uri.AbsoluteUri;
                internetLocation = RemoteLocation.Parse(address, InternetProviders);
            }
            catch (Exception ex) when (ex is RemoteConnectionException or UriFormatException)
            {
                // The form reports a static validation message.
            }
        }

        InternetCredentials.SetLocation(internetLocation);
        UpdateInternetValidation();
    }

    private bool TryGetInternetPort(out int port)
    {
        if (string.IsNullOrEmpty(InternetPort))
        {
            port = DefaultInternetPort;
            return true;
        }

        return int.TryParse(InternetPort, NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
            port is >= 1 and <= 65535;
    }

    private (ValidationTarget Target, string Message)? GetInternetValidationFailure()
    {
        if (internetLocation is null)
        {
            var key = string.IsNullOrWhiteSpace(InternetHost)
                ? TextKeys.AddressIsRequiredForInternetSource
                : !TryGetInternetPort(out _) ? UiKeys.InvalidInternetPort : UiKeys.InvalidInternetHost;
            return (ValidationTarget.InternetAddress, LocalizationService.Current.Get(key));
        }

        if (IsSmbInternetProtocol && internetLocation.Path == "/")
        {
            return (ValidationTarget.InternetPath, LocalizationService.Current.Get(UiKeys.InternetShareRequired));
        }

        return InternetCredentials.CanConfirm
            ? null
            : (ValidationTarget.InternetCredentials, LocalizationService.Current.Get(UiKeys.InternetCredentialsRequired));
    }
}
