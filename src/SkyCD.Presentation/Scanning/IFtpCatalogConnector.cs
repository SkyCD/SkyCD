using System;
using System.Collections.Generic;
using System.Threading;

namespace SkyCD.Presentation.Scanning;

public interface IFtpRemoteSession : IDisposable
{
    IReadOnlyList<FtpRemoteEntry> ListDirectory(string path);
}

public interface IFtpCatalogConnector
{
    IFtpRemoteSession Connect(FtpLocation location, string username, string password, CancellationToken cancellationToken);
}
