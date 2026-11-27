using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Plugins;
using SkyCD.SDK.Capabilities.Catalog;

namespace SkyCD.Plugin.Host.Catalog;

/// <summary>
/// Host facade that runs every post-import hook in isolation, folding the resulting warnings together and
/// never letting one slow or failing plugin abort the import batch.
/// </summary>
public sealed class CatalogImportExtensionManager
{
    private readonly IReadOnlyList<ICatalogImportPluginCapability> _capabilities;

    public CatalogImportExtensionManager(IEnumerable<ICatalogImportPluginCapability> importCapabilities)
    {
        _capabilities = importCapabilities.ToList();
    }

    public async Task<CatalogImportResult> RunAsync(
        IReadOnlyList<CatalogEntry> entries,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var affected = 0;

        foreach (var capability in _capabilities)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try
            {
                var outcome = await capability.OnEntriesImportedAsync(entries, linkedCts.Token).ConfigureAwait(false);
                if (outcome is null)
                {
                    continue;
                }

                warnings.AddRange(outcome.Warnings);
                affected += outcome.AffectedEntries;
            }
            catch (OperationCanceledException exception) when (timeoutCts.IsCancellationRequested)
            {
                PluginFailure.Publish(capability.GetType(), "import", exception);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(capability.GetType(), "import", exception);
            }
        }

        return new CatalogImportResult(warnings, affected);
    }
}
