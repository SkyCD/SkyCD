using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Scanning;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class LoginDialogViewModel : ObservableObject
{
    private readonly SftpHostKeyService hostKeys;
    private IRemoteCatalogConnector provider = RemoteCatalogProviderRegistry.Default.Get("ftp");
    private RemoteLocation? location;
    private SftpHostKeyInfo? presentedHostKey;

    public LoginDialogViewModel() : this(SftpHostKeyService.Default)
    {
    }

    public LoginDialogViewModel(SftpHostKeyService hostKeys) =>
        this.hostKeys = hostKeys ?? throw new ArgumentNullException(nameof(hostKeys));

    public bool CanConfirm => provider.CanConfirmCredentials(
        new RemoteCredentials(Username, Password, UseAnonymous, AuthMethod, KeyFilePath, KeyFilePassphrase));

    public bool IsCredentialEntryEnabled => !UseAnonymous;

    public bool IsSftp => string.Equals(provider.Scheme, "sftp", StringComparison.OrdinalIgnoreCase);

    public bool IsFtp =>
        string.Equals(provider.Scheme, "ftp", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider.Scheme, "ftps", StringComparison.OrdinalIgnoreCase);

    public bool IsAnonymousAvailable => !IsSftp;

    public bool IsKeyAuthentication => IsSftp && AuthMethodIndex == 1;

    public bool IsPasswordAuthentication => !IsKeyAuthentication;

    public RemoteAuthMethod AuthMethod =>
        AuthMethodIndex == 1 ? RemoteAuthMethod.PrivateKey : RemoteAuthMethod.Password;

    public string[] AuthMethodNames =>
    [
        LocalizationService.Current.Get(UiKeys.Password),
        LocalizationService.Current.Get(UiKeys.PrivateKey),
    ];

    /// <summary>TLS mode chosen in this dialog; the scheme of the address selects the default.</summary>
    public RemoteTlsMode TlsMode => (RemoteTlsMode)TlsModeIndex;

    public string[] TlsModeNames =>
    [
        LocalizationService.Current.Get(UiKeys.TlsOff),
        LocalizationService.Current.Get(UiKeys.TlsExplicit),
        LocalizationService.Current.Get(UiKeys.TlsImplicit),
    ];

    /// <summary>
    /// The address to scan: the parsed location with the port chosen in this dialog and, when the
    /// chosen TLS mode differs from the scheme default, an explicit <c>tls=</c> query parameter.
    /// </summary>
    public string EffectiveAddress
    {
        get
        {
            if (location is null)
            {
                return string.Empty;
            }

            var address = new UriBuilder(location.Scheme, location.Host, Port, location.Path).Uri.GetComponents(
                UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
            return TlsMode == provider.DefaultTlsMode
                ? address
                : address + "?tls=" + TlsMode switch
                {
                    RemoteTlsMode.Explicit => "explicit",
                    RemoteTlsMode.Implicit => "implicit",
                    _ => "off",
                };
        }
    }

    [ObservableProperty] private string username = string.Empty;

    [ObservableProperty] private string password = string.Empty;

    [ObservableProperty] private bool useAnonymous;

    [ObservableProperty] private bool dialogAccepted;

    [ObservableProperty] private int authMethodIndex;

    [ObservableProperty] private int tlsModeIndex;

    [ObservableProperty] private string keyFilePath = string.Empty;

    [ObservableProperty] private string keyFilePassphrase = string.Empty;

    [ObservableProperty] private int port = 21;

    [ObservableProperty] private string hostKeyStatus = string.Empty;

    [ObservableProperty] private string? hostKeyFingerprint;

    [ObservableProperty] private bool hostKeyIsTrusted;

    [ObservableProperty] private string hostKeyTrustButtonLabel = string.Empty;

    public static LoginDialogViewModel FromAddress(
        string? address,
        RemoteCatalogProviderRegistry? registry = null,
        SftpHostKeyService? hostKeys = null)
    {
        registry ??= RemoteCatalogProviderRegistry.Default;
        var location = RemoteLocation.Parse(address, registry);
        var viewModel = new LoginDialogViewModel(hostKeys ?? SftpHostKeyService.Default)
        {
            provider = registry.Get(location.Scheme),
            location = location,
            Port = location.Port,
            TlsModeIndex = (int)location.TlsMode,
        };
        if (RemoteLocation.TryReadUserInfo(address, out var username, out var password))
        {
            viewModel.Username = username;
            viewModel.Password = password;
        }

        if (viewModel.IsSftp)
        {
            viewModel.RefreshHostKeyState();
        }

        return viewModel;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        DialogAccepted = true;
    }

    [RelayCommand]
    private async Task CheckHostKeyAsync()
    {
        if (location is null)
        {
            return;
        }

        var host = location.Host;
        HostKeyStatus = LocalizationService.Current.Format(TextKeys.HostKeyChecking, host);
        HostKeyFingerprint = null;
        presentedHostKey = null;
        NotifyHostKeyChanged();

        try
        {
            var presented = await Task.Run(() => hostKeys.FetchHostKey(host, Port, CancellationToken.None));
            presentedHostKey = presented;
            HostKeyFingerprint = presented.Fingerprint;
            SetHostKeyState(hostKeys.Evaluate(host, Port, presented.Fingerprint));
        }
        catch (Exception ex)
        {
            HostKeyIsTrusted = false;
            HostKeyTrustButtonLabel = LocalizationService.Current.Get(UiKeys.TrustHostKey);
            HostKeyStatus = LocalizationService.Current.Format(
                TextKeys.HostKeyCheckFailed, host, SecretSanitizer.Sanitize(ex.Message));
            NotifyHostKeyChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanTrustHostKey))]
    private void TrustHostKey()
    {
        if (location is null || presentedHostKey is null)
        {
            return;
        }

        hostKeys.Trust(location.Host, Port, presentedHostKey);
        SetHostKeyState(SftpHostKeyState.Trusted);
    }

    private bool CanTrustHostKey() => presentedHostKey is not null && !HostKeyIsTrusted;

    private void RefreshHostKeyState()
    {
        if (location is null)
        {
            return;
        }

        var record = hostKeys.Find(location.Host, Port);
        if (record is null)
        {
            HostKeyFingerprint = null;
            SetHostKeyState(SftpHostKeyState.Unknown);
            return;
        }

        HostKeyFingerprint = record.Fingerprint;
        SetHostKeyState(SftpHostKeyState.Trusted);
    }

    private void SetHostKeyState(SftpHostKeyState state)
    {
        HostKeyIsTrusted = state == SftpHostKeyState.Trusted;
        HostKeyStatus = state switch
        {
            SftpHostKeyState.Trusted => LocalizationService.Current.Get(TextKeys.HostKeyStatusTrusted),
            SftpHostKeyState.Changed => LocalizationService.Current.Get(TextKeys.HostKeyStatusChanged),
            _ => LocalizationService.Current.Get(TextKeys.HostKeyStatusUnknown),
        };
        HostKeyTrustButtonLabel = LocalizationService.Current.Get(
            state == SftpHostKeyState.Changed ? UiKeys.ReplaceHostKey : UiKeys.TrustHostKey);
        NotifyHostKeyChanged();
    }

    private void NotifyHostKeyChanged()
    {
        TrustHostKeyCommand.NotifyCanExecuteChanged();
    }

    partial void OnUsernameChanged(string value)
    {
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnPasswordChanged(string value)
    {
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnUseAnonymousChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCredentialEntryEnabled));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnAuthMethodIndexChanged(int value)
    {
        OnPropertyChanged(nameof(AuthMethod));
        OnPropertyChanged(nameof(IsKeyAuthentication));
        OnPropertyChanged(nameof(IsPasswordAuthentication));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnTlsModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(TlsMode));
    }

    partial void OnKeyFilePathChanged(string value)
    {
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnKeyFilePassphraseChanged(string value)
    {
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnPortChanged(int value)
    {
        presentedHostKey = null;
        HostKeyFingerprint = null;
        if (IsSftp)
        {
            RefreshHostKeyState();
        }
    }
}
