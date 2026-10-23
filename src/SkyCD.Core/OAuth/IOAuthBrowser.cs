using System;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Core.OAuth;

/// <summary>Owns a callback listener before opening the external browser.</summary>
public interface IOAuthBrowser
{
    Task<Uri> AuthorizeAsync(Func<Uri, Uri> buildAuthorizationUri, int port, CancellationToken cancellationToken);
}
