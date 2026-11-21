using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Catalog;
using SkyCD.Plugin.Abstractions.Capabilities.Import;
using SkyCD.Plugin.Host.Catalog;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class CatalogImportExtensionManagerTests
{
    private static IReadOnlyList<CatalogEntry> Entries() =>
        [new CatalogEntry("1", "name", "a/b", null, CatalogEntryKind.File, 1, new Dictionary<string, string>())];

    [Fact]
    public async Task RunAsync_AggregatesWarningsAndSkipsNullAndIsolatesFailure()
    {
        var ok = new FakeImportCapability(new ImportOutcome(["warn-a"], 1));
        var none = new FakeImportCapability(null);
        var bad = new FakeImportCapability(throws: true);

        var manager = new CatalogImportExtensionManager([ok, none, bad]);
        var result = await manager.RunAsync(Entries(), TimeSpan.FromSeconds(1));

        Assert.Equal(new[] { "warn-a" }, result.Warnings.ToArray());
        Assert.Equal(1, result.AffectedEntries);
    }

    [Fact]
    public async Task RunAsync_TimesOutSlowPluginAndContinues()
    {
        var slow = new FakeImportCapability(new ImportOutcome(["late"], 99), delayMs: 5000);
        var fast = new FakeImportCapability(new ImportOutcome(["fast"], 2));

        var manager = new CatalogImportExtensionManager([slow, fast]);
        var result = await manager.RunAsync(Entries(), TimeSpan.FromMilliseconds(50));

        Assert.DoesNotContain("late", result.Warnings);
        Assert.Contains("fast", result.Warnings);
        Assert.Equal(2, result.AffectedEntries);
    }

    private sealed class FakeImportCapability(ImportOutcome? outcome = null, int delayMs = 0, bool throws = false)
        : ICatalogImportPluginCapability
    {
        public async Task<ImportOutcome?> OnEntriesImportedAsync(
            IReadOnlyList<CatalogEntry> entries, CancellationToken cancellationToken = default)
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }

            if (throws)
            {
                throw new InvalidOperationException("boom");
            }

            return outcome;
        }
    }
}
