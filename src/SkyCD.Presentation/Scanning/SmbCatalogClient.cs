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
    private readonly ISmbNetworkShare networkShares;

    public SmbCatalogConnector()
        : this(new RealScanFileSystem(), static () => OperatingSystem.IsWindows(), new WindowsSmbNetworkShare())
    {
    }

    /// <summary>Builds the connector; <paramref name="fileSystem"/> is the test seam for a fake session.</summary>
    public SmbCatalogConnector(IScanFileSystem fileSystem)
        : this(fileSystem, static () => OperatingSystem.IsWindows(), new WindowsSmbNetworkShare())
    {
    }

    /// <summary>
    /// Test seam: <paramref name="isPlatformSupported"/> replaces the Windows-only check and
    /// <paramref name="networkShares"/> replaces the network-provider connection handling.
    /// </summary>
    internal SmbCatalogConnector(
        IScanFileSystem fileSystem,
        Func<bool> isPlatformSupported,
        ISmbNetworkShare? networkShares = null)
    {
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        this.isPlatformSupported = isPlatformSupported ?? throw new ArgumentNullException(nameof(isPlatformSupported));
        this.networkShares = networkShares ?? new WindowsSmbNetworkShare();
    }

    public string Scheme => "smb";

    public int DefaultPort => 445;

    /// <summary>The Windows redirector secures the transport; FTPS TLS modes do not apply.</summary>
    public RemoteTlsMode DefaultTlsMode => RemoteTlsMode.Off;

    /// <summary>Windows share paths are case-insensitive, like the local scan on Windows.</summary>
    public StringComparer PathComparer => StringComparer.OrdinalIgnoreCase;

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
        Action? releaseConnection = null;
        if (!credentials.Anonymous)
        {
            // Explicit credentials are attached to the share connection by the Windows
            // network provider; anonymous keeps the current logon session (or guest access).
            try
            {
                networkShares.Connect(shareRoot, credentials.Domain, resolved.Username, resolved.Password);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw TranslateException(ex, shareRoot, location, isListing: false);
            }

            var domain = credentials.Domain;
            var user = resolved.Username;
            releaseConnection = () => networkShares.Disconnect(shareRoot, domain, user);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new SmbRemoteSession(fileSystem, location, cancellationToken, releaseConnection);
        }
        catch
        {
            // A session that never started must not keep the share connection alive.
            releaseConnection?.Invoke();
            throw;
        }
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

/// <summary>
/// Share connections created for explicit credentials. Implementations must throw
/// <see cref="Win32Exception"/> with the network-provider error code on failure and must
/// never throw from <see cref="Disconnect"/>.
/// </summary>
internal interface ISmbNetworkShare
{
    void Connect(string uncShareRoot, string? domain, string username, string password);

    void Disconnect(string uncShareRoot, string? domain, string username);
}

/// <summary>
/// Attaches explicit credentials to Windows share connections (WNetAddConnection2) and tracks
/// ownership so a scan releases only connections it created: connections that were already
/// present (for example mounted by another application) are left alone, and connections are
/// cancelled only after the last session that uses them has finished (#626 review).
/// </summary>
internal sealed class WindowsSmbNetworkShare : ISmbNetworkShare
{
    private readonly object gate = new();
    private readonly Dictionary<(string Share, string User), int> activeUses = new();
    private readonly HashSet<(string Share, string User)> owned = new();

    public void Connect(string uncShareRoot, string? domain, string username, string password)
    {
        lock (gate)
        {
            var key = (uncShareRoot, SmbNetworkConnection.FormatUser(domain, username));
            if (activeUses.TryGetValue(key, out var uses))
            {
                activeUses[key] = uses + 1;
                return;
            }

            if (IsShareConnected(uncShareRoot))
            {
                // Joined a connection somebody else established: never cancel it on release.
                activeUses[key] = 1;
                return;
            }

            var code = SmbNetworkConnection.AddConnection(uncShareRoot, domain, username, password);
            if (code != 0)
            {
                throw new Win32Exception(code);
            }

            activeUses[key] = 1;
            owned.Add(key);
        }
    }

    public void Disconnect(string uncShareRoot, string? domain, string username)
    {
        var cancel = false;
        lock (gate)
        {
            var key = (uncShareRoot, SmbNetworkConnection.FormatUser(domain, username));
            if (!activeUses.TryGetValue(key, out var uses))
            {
                return;
            }

            if (uses > 1)
            {
                activeUses[key] = uses - 1;
                return;
            }

            activeUses.Remove(key);
            cancel = owned.Remove(key);
        }

        if (cancel)
        {
            SmbNetworkConnection.CancelConnection(uncShareRoot);
        }
    }

    private static bool IsShareConnected(string uncShareRoot) =>
        SmbNetworkConnection.IsShareConnected(uncShareRoot);
}

/// <summary>Raw mpr.dll network-provider calls used by <see cref="WindowsSmbNetworkShare"/>.</summary>
internal static class SmbNetworkConnection
{
    /// <summary>Builds the account name the Windows network provider expects: "domain\user".</summary>
    internal static string FormatUser(string? domain, string username) =>
        string.IsNullOrWhiteSpace(domain) || username.Contains('\\', StringComparison.Ordinal)
            ? username
            : domain.Trim() + "\\" + username;

    public static int AddConnection(string uncShareRoot, string? domain, string username, string password)
    {
        var resource = new NETRESOURCE
        {
            dwType = RESOURCETYPE_DISK,
            lpRemoteName = uncShareRoot,
        };
        return WNetAddConnection2(ref resource, password, FormatUser(domain, username), 0);
    }

    public static void CancelConnection(string uncShareRoot)
    {
        try
        {
            WNetCancelConnection2(uncShareRoot, 0, true);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or IOException)
        {
            // Releasing a connection must never fail the scan.
        }
    }

    /// <summary>True when the logon session already has this share connected.</summary>
    public static bool IsShareConnected(string uncShareRoot)
    {
        if (WNetOpenEnum(RESOURCE_CONNECTED, RESOURCETYPE_DISK, 0, null, out var handle) != 0)
        {
            return false;
        }

        try
        {
            var buffer = Marshal.AllocHGlobal(BufferSize);
            try
            {
                while (true)
                {
                    var count = -1;
                    var size = BufferSize;
                    var code = WNetEnumResource(handle, ref count, buffer, ref size);
                    if (code == ERROR_NO_MORE_ITEMS || code != 0 || count <= 0)
                    {
                        return false;
                    }

                    var stride = Marshal.SizeOf<NETRESOURCE>();
                    for (var i = 0; i < count; i++)
                    {
                        var entry = Marshal.PtrToStructure<NETRESOURCE>(IntPtr.Add(buffer, i * stride));
                        if (entry.lpRemoteName is not null &&
                            entry.lpRemoteName.Equals(uncShareRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            WNetCloseEnum(handle);
        }
    }

    private const int RESOURCETYPE_DISK = 1;
    private const int RESOURCE_CONNECTED = 1;
    private const int ERROR_NO_MORE_ITEMS = 259;
    private const int BufferSize = 64 * 1024;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string? lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NETRESOURCE netResource, string? password, string? userName, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetOpenEnum(int scope, int type, int usage, IntPtr? resources, out IntPtr enumHandle);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetEnumResource(IntPtr enumHandle, ref int count, IntPtr buffer, ref int bufferSize);

    [DllImport("mpr.dll")]
    private static extern int WNetCloseEnum(IntPtr enumHandle);
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
    private readonly Action? releaseConnection;

    internal SmbRemoteSession(
        IScanFileSystem fileSystem,
        RemoteLocation location,
        CancellationToken cancellationToken,
        Action? releaseConnection)
    {
        this.fileSystem = fileSystem;
        this.location = location;
        this.cancellationToken = cancellationToken;
        this.releaseConnection = releaseConnection;
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
        try
        {
            releaseConnection?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or Win32Exception)
        {
            // A connection that cannot be released is already gone; disposal must not throw.
        }
    }
}
