using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
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

    [ObservableProperty] private string internetPath = string.Empty;

    public bool IsSmbInternetProtocol => InternetProtocol == "smb";

    public RemoteScanOptions CreateInternetScanOptions(string? targetParentId) =>
        new(InternetCredentials.EffectiveAddress,
            InternetCredentials.Username,
            InternetCredentials.IsPasswordAuthentication ? InternetCredentials.Password : null,
            InternetCredentials.UseAnonymous,
            string.IsNullOrWhiteSpace(MediaName) ? null : MediaName,
            targetParentId,
            IncludeExtendedInfo,
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

        var provider = InternetProviders.Get(location.Scheme);
        InternetProtocol = location.Scheme;
        InternetHost = location.Port == provider.DefaultPort ? location.Host : $"{location.Host}:{location.Port}";
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
        OnPropertyChanged(nameof(IsSmbInternetProtocol));
        UpdateInternetLocation();
    }

    partial void OnInternetHostChanged(string value) => UpdateInternetLocation();

    partial void OnInternetPathChanged(string value) => UpdateInternetLocation();

    private void OnInternetCredentialsChanged(object? sender, EventArgs e) => RecomputeValidation();

    private void UpdateInternetLocation()
    {
        internetLocation = null;
        var host = InternetHost.Trim();
        if (host.Length > 0 && !host.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) &&
            host.IndexOfAny(['/', '\\', '@', '?', '#', '%']) < 0)
        {
            var path = RemoteLocation.EscapePath(InternetPath.TrimStart('/'));
            try
            {
                if (Uri.TryCreate($"{InternetProtocol}://{host}", UriKind.Absolute, out var endpoint) &&
                    endpoint.Port != 0)
                {
                    internetLocation = RemoteLocation.Parse($"{InternetProtocol}://{host}/{path}", InternetProviders);
                }
            }
            catch (Exception ex) when (ex is RemoteConnectionException or UriFormatException)
            {
                // Validation uses a static message so even a URL accidentally typed into the host field stays private.
            }
        }

        InternetCredentials.SetLocation(internetLocation);
        RecomputeValidation();
    }

    private string? GetInternetValidationMessage()
    {
        var key = internetLocation is null
            ? (string.IsNullOrWhiteSpace(InternetHost) ? TextKeys.AddressIsRequiredForInternetSource : UiKeys.InvalidInternetHost)
            : IsSmbInternetProtocol && internetLocation.Path == "/"
                ? UiKeys.InternetShareRequired
                : !InternetCredentials.CanConfirm ? UiKeys.InternetCredentialsRequired : null;
        return key is null ? null : LocalizationService.Current.Get(key);
    }
}
