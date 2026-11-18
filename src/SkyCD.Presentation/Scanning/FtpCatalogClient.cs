using System;
using System.Collections.Generic;
using System.Threading;
using FluentFTP;
using FluentFTP.Exceptions;
using SkyCD.Presentation.Operations;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Establishes a catalog session on top of a <see cref="FluentFTP.FtpClient"/> (#580).
/// The socket, the UTF-8 negotiation, the passive data channel and the reply/listing parsing
/// belong to FluentFTP; this class only maps the negotiated state onto the catalog session
/// contract and onto the SkyCD exception semantics.
/// </summary>
public sealed class FtpCatalogConnector : IRemoteCatalogConnector
{
    public string Scheme => "ftp";

    public int DefaultPort => 21;

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
            throw new RemoteAuthenticationException("ftp");
        }

        return resolved;
    }

    public RemoteConnectionException ConnectionFailed(RemoteLocation location, Exception? cause = null, string? detail = null)
    {
        return new RemoteConnectionException("ftp", $"{location.Host}:{location.Port}", cause,
            detail is null ? null : $" ({detail})");
    }

    /// <summary>Seconds a control or data operation may stall before it is treated as a disconnect.</summary>
    internal const int TransferTimeoutSeconds = 20;

    /// <summary>Seconds a TCP connect may take before it is treated as a disconnect.</summary>
    internal const int ConnectTimeoutSeconds = 15;

    public IRemoteCatalogSession Connect(RemoteLocation location, string username, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);

        RejectLineBreaks(username, password, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var client = new FtpClient(location.Host, username, password, location.Port, CreateConfig());

        try
        {
            // FluentFTP owns the greeting, FEAT/UTF8 negotiation, TYPE I and the login exchange.
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
            throw TranslateConnectFailure(ex, location, cancellationToken);
        }

        return new FtpRemoteSession(client, location, cancellationToken);
    }

    /// <summary>
    /// Transport configuration for the catalog scan. Every value here is a deliberate choice:
    /// the transport must behave exactly like the traversal contract expects.
    /// </summary>
    public static FtpConfig CreateConfig() =>
        new()
        {
            // Supported schemes are decided by RemoteLocation; the transport speaks plain FTP only.
            EncryptionMode = FtpEncryptionMode.None,

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

    /// <summary>Maps a connect failure onto the SkyCD FTP exception semantics.</summary>
    internal static Exception TranslateConnectFailure(
        Exception exception,
        RemoteLocation location,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (exception is FluentFTP.Exceptions.FtpAuthenticationException)
        {
            return new RemoteAuthenticationException("ftp");
        }

        return ConnectionFailed(exception, location, cancellationToken);
    }

    /// <summary>Maps a listing failure onto the SkyCD FTP exception semantics.</summary>
    internal static Exception TranslateListingFailure(
        Exception exception,
        string path,
        RemoteLocation location,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (exception is FluentFTP.Exceptions.FtpAuthenticationException)
        {
            return new RemoteAuthenticationException("ftp");
        }

        if (exception is FtpCommandException command)
        {
            if (IsMissingDirectoryReply(command))
            {
                return new RemoteDirectoryNotFoundException(path);
            }

            if (IsListingRefusedReply(command))
            {
                return new RemoteListingNotSupportedException(path, "list-directory", "ftp");
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
        return new RemoteConnectionException("ftp", target, exception, detail.Length > 0 ? " " + detail : null);
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

    private static void RejectLineBreaks(string username, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (username.IndexOfAny(['\r', '\n']) >= 0 || password.IndexOfAny(['\r', '\n']) >= 0)
        {
            // A line break in a credential is command injection on the control channel, not a login.
            throw new RemoteAuthenticationException("ftp");
        }
    }
}

/// <summary>
/// A connected catalog session. Listings are parsed by FluentFTP; the projection onto
/// <see cref="RemoteEntry"/> keeps the catalog names, sizes, permissions and UTC timestamps.
/// </summary>
internal sealed class FtpRemoteSession : IRemoteCatalogSession
{
    private readonly FtpClient client;
    private readonly RemoteLocation location;
    private readonly CancellationToken cancellationToken;

    internal FtpRemoteSession(FtpClient client, RemoteLocation location, CancellationToken cancellationToken)
    {
        this.client = client;
        this.location = location;
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
            throw FtpCatalogConnector.TranslateListingFailure(ex, normalized, location, cancellationToken);
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
