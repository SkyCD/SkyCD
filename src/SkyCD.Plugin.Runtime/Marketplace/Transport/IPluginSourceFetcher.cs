using System;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Runtime.Marketplace.Transport;

/// <summary>The injectable HTTP boundary of the plugin catalog service.</summary>
public interface IPluginSourceFetcher
{
    /// <summary>
    /// Reads one remote JSON document anonymously over HTTPS and returns its text. Implementations must
    /// reject non-HTTPS addresses and redirects, enforce <paramref name="maxBytes"/> while reading, honor
    /// <paramref name="cancellationToken"/> and throw the documented source exceptions on failure.
    /// </summary>
    Task<string> GetStringAsync(Uri address, long maxBytes, CancellationToken cancellationToken);
}
