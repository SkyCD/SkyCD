using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using SkyCD.Presentation.Operations;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Establishes a catalog session on top of a <see cref="Renci.SshNet.SftpClient"/>.
/// The SSH transport, the key exchange and the SFTP protocol belong to SSH.NET; this class
/// maps the negotiated state onto the catalog session contract and onto the SkyCD exception
/// semantics, and enforces host-key trust through <see cref="SftpHostKeyService"/>.
/// </summary>
public sealed class SftpCatalogConnector : IRemoteCatalogConnector
{
    private readonly SftpHostKeyService hostKeys;

    public SftpCatalogConnector() : this(SftpHostKeyService.Default)
    {
    }

    public SftpCatalogConnector(SftpHostKeyService hostKeys) =>
        this.hostKeys = hostKeys ?? throw new ArgumentNullException(nameof(hostKeys));

    public string Scheme => "sftp";

    public int DefaultPort => 22;

    public string NormalizePath(string? path) => FtpPath.Normalize(path);

    public bool CanConfirmCredentials(RemoteCredentials credentials) =>
        !string.IsNullOrWhiteSpace(credentials.Username) &&
        (credentials.AuthMethod == RemoteAuthMethod.PrivateKey
            ? !string.IsNullOrWhiteSpace(credentials.KeyFilePath)
            : !string.IsNullOrWhiteSpace(credentials.Password));

    public (string Username, string Password) ResolveCredentials(RemoteCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var username = credentials.Username?.Trim() ?? string.Empty;
        if (credentials.AuthMethod == RemoteAuthMethod.PrivateKey)
        {
            if (username.Length == 0 || string.IsNullOrWhiteSpace(credentials.KeyFilePath))
            {
                throw new RemoteAuthenticationException("sftp");
            }

            return (username, string.Empty);
        }

        if (username.Length == 0 || string.IsNullOrEmpty(credentials.Password))
        {
            throw new RemoteAuthenticationException("sftp");
        }

        return (username, credentials.Password);
    }

    public RemoteConnectionException ConnectionFailed(RemoteLocation location, Exception? cause = null, string? detail = null)
    {
        return new RemoteConnectionException("sftp", $"{location.Host}:{location.Port}", cause,
            detail is null ? null : $" ({detail})");
    }

    /// <summary>Seconds the SSH handshake and authentication may take before it is treated as a disconnect.</summary>
    internal const int ConnectTimeoutSeconds = 15;

    /// <summary>Seconds an SFTP request may stall before it is treated as a disconnect.</summary>
    internal const int TransferTimeoutSeconds = 20;

    public IRemoteCatalogSession Connect(RemoteLocation location, RemoteCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(credentials);

        var resolved = ResolveCredentials(credentials);
        cancellationToken.ThrowIfCancellationRequested();

        var client = CreateClient(location, resolved.Username, credentials);
        var hostKeyState = SftpHostKeyState.Unknown;
        string? hostKeyFingerprint = null;
        client.HostKeyReceived += (_, e) =>
        {
            hostKeyFingerprint = SftpHostKeyInfo.FormatFingerprint(e.FingerPrintSHA256);
            hostKeyState = hostKeys.Evaluate(location.Host, location.Port, hostKeyFingerprint);
            e.CanTrust = hostKeyState == SftpHostKeyState.Trusted;
        };

        try
        {
            // SSH.NET owns the transport handshake, the key exchange and the authentication.
            client.Connect();
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            client.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            client.Dispose();

            if (hostKeyFingerprint is not null && hostKeyState != SftpHostKeyState.Trusted)
            {
                throw new RemoteHostKeyException(
                    $"{location.Host}:{location.Port}",
                    hostKeyFingerprint,
                    hostKeyState == SftpHostKeyState.Changed);
            }

            throw TranslateConnectFailure(ex, location, cancellationToken);
        }

        return new SftpRemoteSession(client, location, cancellationToken);
    }

    private static SftpClient CreateClient(RemoteLocation location, string username, RemoteCredentials credentials)
    {
        ConnectionInfo connectionInfo;
        if (credentials.AuthMethod == RemoteAuthMethod.PrivateKey)
        {
            PrivateKeyFile keyFile;
            try
            {
                keyFile = string.IsNullOrEmpty(credentials.KeyFilePassphrase)
                    ? new PrivateKeyFile(credentials.KeyFilePath!)
                    : new PrivateKeyFile(credentials.KeyFilePath!, credentials.KeyFilePassphrase);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new RemoteAuthenticationException("sftp");
            }

            connectionInfo = new PrivateKeyConnectionInfo(location.Host, location.Port, username, keyFile);
        }
        else
        {
            connectionInfo = new PasswordConnectionInfo(location.Host, location.Port, username, credentials.Password!);
        }

        connectionInfo.Timeout = TimeSpan.FromSeconds(ConnectTimeoutSeconds);
        return new SftpClient(connectionInfo)
        {
            OperationTimeout = TimeSpan.FromSeconds(TransferTimeoutSeconds),
        };
    }

    /// <summary>Maps a connect failure onto the SkyCD SFTP exception semantics.</summary>
    internal static Exception TranslateConnectFailure(
        Exception exception,
        RemoteLocation location,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (exception is SshAuthenticationException)
        {
            return new RemoteAuthenticationException("sftp");
        }

        return ConnectionFailed(exception, location, cancellationToken);
    }

    /// <summary>Maps a listing failure onto the SkyCD SFTP exception semantics.</summary>
    internal static Exception TranslateListingFailure(
        Exception exception,
        string path,
        RemoteLocation location,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (exception is SshAuthenticationException)
        {
            return new RemoteAuthenticationException("sftp");
        }

        if (exception is SftpPathNotFoundException)
        {
            return new RemoteDirectoryNotFoundException(path);
        }

        if (exception is SftpPermissionDeniedException)
        {
            return new RemoteListingNotSupportedException(path, "list-directory", "sftp");
        }

        return ConnectionFailed(exception, location, cancellationToken);
    }

    /// <summary>Builds the connection exception the scan classifies as a transient disconnect.</summary>
    internal static RemoteConnectionException ConnectionFailed(
        Exception exception,
        RemoteLocation location,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = $"{location.Host}:{location.Port}";
        var detail = DescribeFailure(exception, location);
        return new RemoteConnectionException("sftp", target, exception, detail.Length > 0 ? " " + detail : null);
    }

    /// <summary>
    /// Builds a sanitized one-line description of a transport failure. Credentials and anything
    /// that looks like a secret are removed before the text can reach a progress log.
    /// </summary>
    internal static string DescribeFailure(Exception exception, RemoteLocation location)
    {
        var detail = exception switch
        {
            SshException => exception.Message,
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
}

/// <summary>
/// A connected catalog session. Listings are parsed by SSH.NET; the projection onto
/// <see cref="RemoteEntry"/> keeps the catalog names, sizes, permissions and UTC timestamps.
/// Symlinked entries are resolved to their target path so the scanner's shared visited-identity
/// logic treats symlinked directories as cycle candidates.
/// </summary>
internal sealed class SftpRemoteSession : IRemoteCatalogSession
{
    private readonly SftpClient client;
    private readonly RemoteLocation location;
    private readonly CancellationToken cancellationToken;

    internal SftpRemoteSession(
        SftpClient client,
        RemoteLocation location,
        CancellationToken cancellationToken)
    {
        this.client = client;
        this.location = location;
        this.cancellationToken = cancellationToken;
    }

    public IReadOnlyList<RemoteEntry> ListDirectory(string path)
    {
        var normalized = FtpPath.Normalize(path);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ISftpFile> listing;
        try
        {
            listing = client.ListDirectory(normalized).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw SftpCatalogConnector.TranslateListingFailure(ex, normalized, location, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return SftpRemoteListingMapper.Map(listing, normalized, ResolveLinkTarget);
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

    /// <summary>Resolves one symlink to its target entry, or null when the link is dangling.</summary>
    private ISftpFile? ResolveLinkTarget(ISftpFile link)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return client.Get(link.FullName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
