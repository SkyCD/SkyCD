using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Threading;
using FluentFTP;
using FluentFTP.Exceptions;
using SkyCD.Presentation.Operations;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Establishes a catalog session on top of a <see cref="FluentFTP.FtpClient"/> (#580).
/// The socket, the UTF-8 negotiation, the passive data channel and the reply/listing parsing
/// belong to FluentFTP; this class only maps the negotiated state onto the catalog session
/// contract and onto the SkyCD exception semantics. One instance serves one scheme: plain
/// <c>ftp://</c> or <c>ftps://</c> with implicit TLS (#623).
/// </summary>
public sealed class FtpCatalogConnector : IRemoteCatalogConnector
{
    private readonly string scheme;
    private readonly int defaultPort;
    private readonly RemoteTlsMode defaultTlsMode;
    private readonly FtpSslValidation? certificateValidation;

    public FtpCatalogConnector() : this("ftp", 21, RemoteTlsMode.Off)
    {
    }

    /// <summary>
    /// Builds a connector for one FTP scheme. <paramref name="certificateValidation"/> is a
    /// test-only trust policy; when null the FluentFTP default applies and every certificate
    /// must validate against the system trust store and the target host name.
    /// </summary>
    internal FtpCatalogConnector(
        string scheme,
        int defaultPort,
        RemoteTlsMode defaultTlsMode,
        FtpSslValidation? certificateValidation = null)
    {
        this.scheme = scheme;
        this.defaultPort = defaultPort;
        this.defaultTlsMode = defaultTlsMode;
        this.certificateValidation = certificateValidation;
    }

    public string Scheme => scheme;

    public int DefaultPort => defaultPort;

    public RemoteTlsMode DefaultTlsMode => defaultTlsMode;

    public string NormalizePath(string? path) => FtpPath.Normalize(path);

    public bool CanConfirmCredentials(RemoteCredentials credentials) =>
        credentials.Anonymous ||
        (!string.IsNullOrWhiteSpace(credentials.Username) &&
         (!string.IsNullOrWhiteSpace(credentials.Password) || FtpCredentials.IsAnonymousUserName(credentials.Username)));

    public (string Username, string Password) ResolveCredentials(RemoteCredentials credentials)
    {
        var resolved = FtpCredentials.Resolve(credentials.Username, credentials.Password, credentials.Anonymous);
        if (!credentials.Anonymous && !FtpCredentials.IsAnonymousUserName(resolved.Username) &&
            string.IsNullOrEmpty(resolved.Password))
        {
            throw new RemoteAuthenticationException(scheme);
        }

        return resolved;
    }

    public RemoteConnectionException ConnectionFailed(RemoteLocation location, Exception? cause = null, string? detail = null)
    {
        return new RemoteConnectionException(scheme, $"{location.Host}:{location.Port}", cause,
            detail is null ? null : $" ({detail})");
    }

    /// <summary>Seconds a control or data operation may stall before it is treated as a disconnect.</summary>
    internal const int TransferTimeoutSeconds = 20;

    /// <summary>Seconds a TCP connect may take before it is treated as a disconnect.</summary>
    internal const int ConnectTimeoutSeconds = 15;

    public IRemoteCatalogSession Connect(RemoteLocation location, RemoteCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(credentials);

        var (username, password) = ResolveCredentials(credentials);
        RejectLineBreaks(username, password, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var validation = new TlsValidationState();
        var client = new FtpClient(location.Host, username, password, location.Port, CreateConfig(location.TlsMode));
        client.ValidateCertificate += (control, args) =>
        {
            certificateValidation?.Invoke(control, args);
            if (!args.Accept)
            {
                validation.CertificateRejected = true;
            }
        };

        try
        {
            // FluentFTP owns the greeting, FEAT/UTF8 negotiation, TYPE I and the login exchange.
            client.Connect();
            cancellationToken.ThrowIfCancellationRequested();

            if (location.TlsMode != RemoteTlsMode.Off)
            {
                NegotiateDataProtection(client, location, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            client.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            client.Dispose();
            throw TranslateConnectFailure(ex, location, validation, cancellationToken);
        }

        return new FtpRemoteSession(client, location, validation, cancellationToken);
    }

    /// <summary>
    /// Transport configuration for the catalog scan. Every value here is a deliberate choice:
    /// the transport must behave exactly like the traversal contract expects.
    /// </summary>
    public static FtpConfig CreateConfig(RemoteTlsMode tlsMode) =>
        new()
        {
            // Supported schemes and TLS modes are decided by RemoteLocation; the transport
            // implements them with FluentFTP's implicit/explicit FTPS (#623).
            EncryptionMode = tlsMode switch
            {
                RemoteTlsMode.Implicit => FtpEncryptionMode.Implicit,
                RemoteTlsMode.Explicit => FtpEncryptionMode.Explicit,
                _ => FtpEncryptionMode.None,
            },

            // Data-channel protection is negotiated explicitly after login (PROT P) so a rejected
            // upgrade is reported precisely; switched on again in NegotiateDataProtection.
            DataConnectionEncryption = false,

            // Passive transfer, EPSV first with PASV as the fallback.
            DataConnectionType = FtpDataConnectionType.AutoPassive,

            ConnectTimeout = ConnectTimeoutSeconds * 1000,
            ReadTimeout = TransferTimeoutSeconds * 1000,
            WriteTimeout = TransferTimeoutSeconds * 1000,
            DataConnectionConnectTimeout = ConnectTimeoutSeconds * 1000,
            DataConnectionReadTimeout = TransferTimeoutSeconds * 1000,
            DataConnectionWriteTimeout = TransferTimeoutSeconds * 1000,

            // FluentFTP requires at least one attempt for upload/download verification.
            // The catalog traversal owns its own bounded retry and reconnect policy (#433).
            RetryAttempts = 1,

            // Listing times are normalized to UTC rather than the machine's time zone.
            TimeConversion = FtpDate.UTC,

            // FluentFTP parses the server's LIST dialect.
            ListingParser = FtpParser.Auto,
            BulkListing = false,

            DisconnectWithQuit = true,
        };

    /// <summary>
    /// Listing options handed to FluentFTP for one directory.
    /// <see cref="FtpListOption.Modify"/> and <see cref="FtpListOption.Size"/> keep the exact
    /// modification time and the size of every entry: a LIST record only carries minutes and
    /// cannot express a directory size, so FluentFTP asks the server (MDTM/SIZE) for the entries
    /// it parsed without them. That costs one round trip per entry, but the catalog must keep the
    /// timestamps the server reports instead of a rounded-down one.
    /// </summary>
    public static FtpListOption ListingOptions => FtpListOption.ForceList | FtpListOption.SizeModify;

    /// <summary>
    /// Upgrades the protected data channel (RFC 4217): PBSZ 0 then PROT P, after which every
    /// transfer is TLS-wrapped. Rejected upgrades fail typed instead of falling back to a
    /// plaintext data channel. FluentFTP sends these itself when DataConnectionEncryption is
    /// set up front; doing it here keeps a rejection distinguishable from any other command
    /// failure during connect.
    /// </summary>
    private static void NegotiateDataProtection(FtpClient client, RemoteLocation location, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pbsz = client.Execute("PBSZ 0");
        cancellationToken.ThrowIfCancellationRequested();
        if (!pbsz.Success)
        {
            throw new RemoteTlsDataProtectionException($"{location.Host}:{location.Port}");
        }

        var prot = client.Execute("PROT P");
        cancellationToken.ThrowIfCancellationRequested();
        if (!prot.Success)
        {
            throw new RemoteTlsDataProtectionException($"{location.Host}:{location.Port}");
        }

        // The server promised protected data channels; hold FluentFTP to that for every transfer.
        client.Config.DataConnectionEncryption = true;
    }

    /// <summary>Maps a connect failure onto the SkyCD FTP exception semantics.</summary>
    internal static Exception TranslateConnectFailure(
        Exception exception,
        RemoteLocation location,
        TlsValidationState validation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (exception is RemoteTlsException)
        {
            return exception;
        }

        if (location.TlsMode != RemoteTlsMode.Off)
        {
            switch (exception)
            {
                // The server refused the AUTH TLS upgrade; plaintext is not a fallback.
                case FtpSecurityNotAvailableException:
                    return new RemoteTlsUpgradeException($"{location.Host}:{location.Port}", exception);
                case FtpInvalidCertificateException:
                    return new RemoteCertificateValidationException($"{location.Host}:{location.Port}", exception);
                case AuthenticationException:
                    return validation.CertificateRejected
                        ? new RemoteCertificateValidationException($"{location.Host}:{location.Port}", exception)
                        : new RemoteTlsHandshakeException($"{location.Host}:{location.Port}", exception);
            }
        }

        if (exception is FluentFTP.Exceptions.FtpAuthenticationException)
        {
            return new RemoteAuthenticationException(location.Scheme);
        }

        return ConnectionFailed(exception, location, cancellationToken);
    }

    /// <summary>Maps a listing failure onto the SkyCD FTP exception semantics.</summary>
    internal static Exception TranslateListingFailure(
        Exception exception,
        string path,
        RemoteLocation location,
        TlsValidationState validation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (exception is RemoteTlsException)
        {
            return exception;
        }

        if (location.TlsMode != RemoteTlsMode.Off)
        {
            switch (exception)
            {
                case FtpInvalidCertificateException:
                    return new RemoteCertificateValidationException($"{location.Host}:{location.Port}", exception);
                case AuthenticationException:
                    return validation.CertificateRejected
                        ? new RemoteCertificateValidationException($"{location.Host}:{location.Port}", exception)
                        : new RemoteTlsHandshakeException($"{location.Host}:{location.Port}", exception);
            }
        }

        if (exception is FluentFTP.Exceptions.FtpAuthenticationException)
        {
            return new RemoteAuthenticationException(location.Scheme);
        }

        if (exception is FtpCommandException command)
        {
            if (IsMissingDirectoryReply(command))
            {
                return new RemoteDirectoryNotFoundException(path);
            }

            if (IsListingRefusedReply(command))
            {
                return new RemoteListingNotSupportedException(path, "list-directory", location.Scheme);
            }
        }

        return ConnectionFailed(exception, location, cancellationToken);
    }

    /// <summary>A reply code that means the directory is gone rather than the connection broken.</summary>
    internal static bool IsMissingDirectoryReply(FtpCommandException exception) =>
        exception.CompletionCode is "550" or "521";

    /// <summary>A reply code that means the server refuses to produce a listing at all.</summary>
    internal static bool IsListingRefusedReply(FtpCommandException exception) =>
        exception.CompletionCode is "500" or "501" or "502" or "504";

    /// <summary>Builds the connection exception the scan classifies as a transient disconnect.</summary>
    internal static RemoteConnectionException ConnectionFailed(
        Exception exception,
        RemoteLocation location,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = $"{location.Host}:{location.Port}";
        var detail = DescribeFailure(exception, location);
        return new RemoteConnectionException(location.Scheme, target, exception, detail.Length > 0 ? " " + detail : null);
    }

    /// <summary>
    /// Builds a sanitized one-line description of a transport failure. Credentials and anything
    /// that looks like a secret are removed before the text can reach a progress log.
    /// </summary>
    internal static string DescribeFailure(Exception exception, RemoteLocation location)
    {
        var detail = exception switch
        {
            FtpCommandException command => $"FTP {command.CompletionCode}: {command.Message}",
            FtpException => exception.Message,
            _ when exception.InnerException is not null => exception.InnerException.Message,
            _ => exception.Message,
        };

        detail = SecretSanitizer.Sanitize(detail ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (detail.Length > 0 && detail.Contains(location.Host, StringComparison.OrdinalIgnoreCase))
        {
            // The target is already part of the message; do not repeat the host with the port.
            detail = detail
                .Replace(location.Host, string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim(' ', ':', '(', ')');
        }

        return detail;
    }

    private void RejectLineBreaks(string username, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (username.IndexOfAny(['\r', '\n']) >= 0 || password.IndexOfAny(['\r', '\n']) >= 0)
        {
            // A line break in a credential is command injection on the control channel, not a login.
            throw new RemoteAuthenticationException(scheme);
        }
    }
}

/// <summary>
/// Records whether a presented TLS certificate was rejected, so a handshake failure can be
/// reported as a certificate problem rather than a generic TLS failure.
/// </summary>
internal sealed class TlsValidationState
{
    public bool CertificateRejected { get; set; }
}

/// <summary>
/// A connected catalog session. Listings are parsed by FluentFTP; the projection onto
/// <see cref="RemoteEntry"/> keeps the catalog names, sizes, permissions and UTC timestamps.
/// </summary>
internal sealed class FtpRemoteSession : IRemoteCatalogSession
{
    private readonly FtpClient client;
    private readonly RemoteLocation location;
    private readonly TlsValidationState validation;
    private readonly CancellationToken cancellationToken;

    internal FtpRemoteSession(
        FtpClient client,
        RemoteLocation location,
        TlsValidationState validation,
        CancellationToken cancellationToken)
    {
        this.client = client;
        this.location = location;
        this.validation = validation;
        this.cancellationToken = cancellationToken;
    }

    public IReadOnlyList<RemoteEntry> ListDirectory(string path)
    {
        var normalized = FtpPath.Normalize(path);
        cancellationToken.ThrowIfCancellationRequested();

        FtpListItem[] listing;
        try
        {
            var options = FtpCatalogConnector.ListingOptions;
            if (!client.HasFeature(FtpCapability.MLST))
            {
                // Without MLST the LIST dialect is the only one; force it so FluentFTP does not
                // spend a round trip on a command the server does not implement.
                options |= FtpListOption.ForceList;
            }

            listing = client.GetListing(normalized, options);
            // FluentFTP absorbs 550 replies as empty listings; the catalog must report them.
            if (listing.Length == 0 && !client.LastReply.Success)
            {
                throw new FtpCommandException(client.LastReply);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw FtpCatalogConnector.TranslateListingFailure(ex, normalized, location, validation, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return FtpRemoteListingMapper.Map(listing, normalized);
    }

    public void Dispose()
    {
        try
        {
            client.Disconnect();
        }
        catch (Exception)
        {
            // A session that cannot say goodbye is already gone; disposal must not throw.
        }

        client.Dispose();
    }
}
