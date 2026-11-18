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

    string NormalizePath(string? path);

    bool CanConfirmCredentials(RemoteCredentials credentials);

    (string Username, string Password) ResolveCredentials(RemoteCredentials credentials);

    RemoteConnectionException ConnectionFailed(RemoteLocation location, Exception? cause = null, string? detail = null);

    IRemoteCatalogSession Connect(RemoteLocation location, string username, string password, CancellationToken cancellationToken);
}
