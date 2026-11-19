using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using SkyCD.Presentation.Operations;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Establishes a catalog session over a Windows SMB share (#626). Addresses are normalized to
/// the seam's path model for both <c>smb://server/share/path</c> and UNC
/// <c>\\server\share\path</c>; the transport is the Windows redirector via
/// <see cref="IScanFileSystem"/> — no SMB protocol code and no extra dependency (the NuGet SMB
/// clients are LGPL and therefore blocked by the dependency license policy).
/// The provider is Windows-only: other platforms fail with the typed
/// <see cref="RemotePlatformNotSupportedException"/>. Read-only: uploads, moves and deletes
/// are out of scope.
/// </summary>
public sealed class SmbCatalogConnector : IRemoteCatalogConnector
{
    private readonly IScanFileSystem fileSystem;
    private readonly Func<bool> isPlatformSupported;

    public SmbCatalogConnector() : this(new RealScanFileSystem(), static () => OperatingSystem.IsWindows())
    {
    }

    /// <summary>Builds the connector; <paramref name="fileSystem"/> is the test seam for a fake session.</summary>
    public SmbCatalogConnector(IScanFileSystem fileSystem)
        : this(fileSystem, static () => OperatingSystem.IsWindows())
    {
    }

    /// <summary>Test seam: <paramref name="isPlatformSupported"/> replaces the Windows-only check.</summary>
    internal SmbCatalogConnector(IScanFileSystem fileSystem, Func<bool> isPlatformSupported)
    {
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        this.isPlatformSupported = isPlatformSupported ?? throw new ArgumentNullException(nameof(isPlatformSupported));
    }

    public string Scheme => "smb";

    public int DefaultPort => 445;

    /// <summary>The Windows redirector secures the transport; FTPS TLS modes do not apply.</summary>
    public RemoteTlsMode DefaultTlsMode => RemoteTlsMode.Off;

    public string NormalizePath(string? path) => FtpPath.Normalize(path);

    public bool CanConfirmCredentials(RemoteCredentials credentials) =>
        credentials.Anonymous ||
        (!string.IsNullOrWhiteSpace(credentials.Username) &&
         !string.IsNullOrWhiteSpace(credentials.Password));

    public (string Username, string Password) ResolveCredentials(RemoteCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (credentials.Anonymous)
        {
            // Guest or the current logon session, whichever the server permits.
            return (string.Empty, string.Empty);
        }

        var username = credentials.Username?.Trim() ?? string.Empty;
        if (username.Length == 0 || string.IsNullOrEmpty(credentials.Password))
        {
            throw new RemoteAuthenticationException(Scheme);
        }

        return (username, credentials.Password);
    }

    public RemoteConnectionException ConnectionFailed(RemoteLocation location, Exception? cause = null, string? detail = null)
    {
        return new RemoteConnectionException(Scheme, SmbPath.ToUnc(location, location.Path), cause,
            detail is null ? null : $" ({detail})");
    }

    public IRemoteCatalogSession Connect(RemoteLocation location, RemoteCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(credentials);

        if (!isPlatformSupported())
        {
            throw new RemotePlatformNotSupportedException(Scheme);
        }

        var resolved = ResolveCredentials(credentials);
        cancellationToken.ThrowIfCancellationRequested();

        var shareRoot = ShareRoot(location);
        if (!credentials.Anonymous)
        {
            // Explicit credentials are attached to the share connection by the Windows
            // network provider; anonymous keeps the current logon session (or guest access).
            SmbNetworkConnection.Establish(shareRoot, credentials.Domain, resolved.Username, resolved.Password);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Share/root validation: one enumeration step fails typed when the share is missing,
        // the server is unreachable or the share refuses the account. A missing subfolder is
        // not a connect failure and is reported by the first listing, like FTP login.
        try
        {
            using var probe = fileSystem.EnumerateDirectories(shareRoot).GetEnumerator();
            probe.MoveNext();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw TranslateException(ex, shareRoot, location, isListing: false);
        }

        return new SmbRemoteSession(fileSystem, location, cancellationToken);
    }

    private static string ShareRoot(RemoteLocation location)
    {
        var trimmed = location.Path.Trim('/');
        var share = trimmed.Length == 0 ? string.Empty : trimmed.Split('/')[0];
        return SmbPath.ToUnc(location, "/" + share);
    }

    /// <summary>
    /// Maps a filesystem or network-provider failure onto the SkyCD remote exception semantics.
    /// Win32 codes reach us through <see cref="IOException.HResult"/> or
    /// <see cref="Win32Exception.NativeErrorCode"/>; a network path that is not found covers
    /// both a missing share and an unreachable server and is reported as a connection failure,
    /// matching FTP's transient retry semantics.
    /// </summary>
    internal static Exception TranslateException(
        Exception exception,
        string uncPath,
        RemoteLocation location,
        bool isListing)
    {
        if (exception is OperationCanceledException or RemoteAuthenticationException
            or RemoteConnectionException or RemoteDirectoryNotFoundException
            or RemoteListingNotSupportedException or RemotePlatformNotSupportedException)
        {
            return exception;
        }

        var code = exception switch
        {
            Win32Exception win32 => win32.NativeErrorCode,
            IOException io => io.HResult & 0xFFFF,
            UnauthorizedAccessException => 5,
            _ => 0,
        };

        var remotePath = FtpPath.Normalize(SmbPath.ToRemotePath(uncPath, location.Host));

        // Inside a listing an access denial is an ACL refusal of one folder and fails the
        // item like FTP's refused listing; at connect time it is a rejected account.
        if (isListing && (code == 5 || exception is UnauthorizedAccessException))
        {
            return new RemoteListingNotSupportedException(remotePath, "list-directory", location.Scheme);
        }

        return code switch
        {
            // ERROR_ACCESS_DENIED, ERROR_INVALID_PASSWORD, ERROR_LOGON_FAILURE.
            5 or 86 or 1326 => new RemoteAuthenticationException(location.Scheme),

            // ERROR_BAD_NETPATH / ERROR_BAD_NET_NAME: missing share or unreachable server.
            // ERROR_NETWORK_UNREACHABLE / ERROR_HOST_UNREACHABLE / ERROR_CONNECTION_UNAVAIL.
            53 or 67 or 1225 or 1231 or 1203 => new RemoteConnectionException(
                location.Scheme, uncPath, exception, Describe(exception)),

            // A folder inside the share is gone.
            _ when exception is DirectoryNotFoundException => new RemoteDirectoryNotFoundException(remotePath),

            _ => new RemoteConnectionException(location.Scheme, uncPath, exception, Describe(exception)),
        };
    }

    private static string? Describe(Exception exception)
    {
        var detail = SecretSanitizer.Sanitize(exception.Message ?? string.Empty)
            .Replace('\r', ' ').Replace('\n', ' ').Trim();
        return detail.Length > 0 ? " " + detail : null;
    }
}

/// <summary>Attaches explicit credentials to a Windows share connection (WNetAddConnection2).</summary>
internal static class SmbNetworkConnection
{
    public static void Establish(string uncShareRoot, string? domain, string username, string password)
    {
        var resource = new NETRESOURCE
        {
            dwType = RESOURCETYPE_DISK,
            lpRemoteName = uncShareRoot,
        };

        var code = WNetAddConnection2(ref resource, password, FormatUser(domain, username), 0);
        if (code != 0)
        {
            throw new Win32Exception(code);
        }
    }

    /// <summary>Builds the account name the Windows network provider expects: "domain\user".</summary>
    internal static string FormatUser(string? domain, string username) =>
        string.IsNullOrWhiteSpace(domain) || username.Contains('\\', StringComparison.Ordinal)
            ? username
            : domain.Trim() + "\\" + username;

    private const int RESOURCETYPE_DISK = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NETRESOURCE netResource, string? password, string? userName, int flags);
}

/// <summary>
/// A connected SMB catalog session. Listings come from the Windows redirector through
/// <see cref="IScanFileSystem"/> and are projected onto <see cref="RemoteEntry"/>.
/// </summary>
internal sealed class SmbRemoteSession : IRemoteCatalogSession
{
    private readonly IScanFileSystem fileSystem;
    private readonly RemoteLocation location;
    private readonly CancellationToken cancellationToken;

    internal SmbRemoteSession(IScanFileSystem fileSystem, RemoteLocation location, CancellationToken cancellationToken)
    {
        this.fileSystem = fileSystem;
        this.location = location;
        this.cancellationToken = cancellationToken;
    }

    public IReadOnlyList<RemoteEntry> ListDirectory(string path)
    {
        var normalized = FtpPath.Normalize(path);
        cancellationToken.ThrowIfCancellationRequested();

        var uncPath = SmbPath.ToUnc(location, normalized);
        try
        {
            var files = fileSystem.EnumerateFiles(uncPath).ToList();
            cancellationToken.ThrowIfCancellationRequested();
            var directories = fileSystem.EnumerateDirectories(uncPath).ToList();
            cancellationToken.ThrowIfCancellationRequested();

            return SmbRemoteListingMapper.Map(files.Concat(directories), normalized, location.Host);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw SmbCatalogConnector.TranslateException(ex, uncPath, location, isListing: true);
        }
    }

    public void Dispose()
    {
        // The Windows share connection outlives the session on purpose: cancelling it could
        // break other connections of the logon session to the same server.
    }
}
