using System;
using System.Collections.Generic;
using System.Threading;

namespace SkyCD.Presentation.Scanning;

public interface IRemoteCatalogSession : IDisposable
{
    IReadOnlyList<RemoteEntry> ListDirectory(string path);
}

public interface IRemoteCatalogConnector
{
    string Scheme { get; }

    int DefaultPort { get; }

    /// <summary>TLS mode this protocol defaults to; an address may override it (see RemoteLocation).</summary>
    RemoteTlsMode DefaultTlsMode { get; }

    /// <summary>
    /// How this protocol compares paths for the shared visited-identity logic: Windows-like
    /// providers (SMB) are case-insensitive, server-defined providers (FTP, SFTP, WebDAV) are not.
    /// </summary>
    StringComparer PathComparer { get; }

    string NormalizePath(string? path);

    bool CanConfirmCredentials(RemoteCredentials credentials);

    (string Username, string Password) ResolveCredentials(RemoteCredentials credentials);

    RemoteConnectionException ConnectionFailed(RemoteLocation location, Exception? cause = null, string? detail = null);

    IRemoteCatalogSession Connect(RemoteLocation location, RemoteCredentials credentials, CancellationToken cancellationToken);
}
