using System;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Ai.GenDesc.Import;

/// <summary>
/// The single network seam every importer uses. It is deliberately narrow so the plugin never performs I/O
/// outside an explicit generation call, and tests can supply recorded fixture bodies without any sockets.
/// </summary>
public interface IWebFetcher
{
    /// <summary>
    /// Fetches a document as text. Implementations must honor the cancellation token and the per-call timeout,
    /// and surface transport or HTTP-status problems as an exception so the orchestrator can isolate that one
    /// source without affecting the others.
    /// </summary>
    Task<string> GetStringAsync(string url, TimeSpan timeout, CancellationToken cancellationToken);
}
