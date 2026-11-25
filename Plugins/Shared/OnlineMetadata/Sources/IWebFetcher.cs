using System;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.OnlineMetadata.Sources;

/// <summary>
/// The single network seam a source plugin uses. It is deliberately narrow so a plugin performs no I/O
/// outside an explicit lookup, and tests can supply recorded fixture bodies without any sockets.
/// </summary>
internal interface IWebFetcher
{
    /// <summary>
    /// Fetches a document as text. Implementations must honor the cancellation token (which carries the
    /// caller's per-source timeout) and surface transport or HTTP-status problems as an exception so the
    /// source can turn them into a failure result.
    /// </summary>
    Task<string> GetStringAsync(string url, CancellationToken cancellationToken);
}
