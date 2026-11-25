using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Host.Media;
using SkyCD.SDK.Capabilities.Media;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class OnlineMetadataExtensionManagerTests
{
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(15);

    private sealed class StubSource : IOnlineMetadataSourceCapability
    {
        private readonly Func<OnlineMetadataRequest, CancellationToken, Task<OnlineMetadataResult>> fetch;

        public StubSource(string sourceId,
            Func<OnlineMetadataRequest, CancellationToken, Task<OnlineMetadataResult>> fetch,
            params string[] knownHosts)
        {
            SourceId = sourceId;
            this.fetch = fetch;
            KnownHosts = knownHosts;
        }

        public string SourceId { get; }

        public string DisplayName => SourceId;

        public IReadOnlyList<string> KnownHosts { get; }

        public Task<OnlineMetadataResult> FetchAsync(OnlineMetadataRequest request,
            CancellationToken cancellationToken) => fetch(request, cancellationToken);
    }

    private static OnlineMetadataProvider Provider(string pluginId, IOnlineMetadataSourceCapability capability) =>
        new(pluginId, "1.0.0", capability);

    private static StubSource Succeeds(string sourceId, string? url, string key, string value) =>
        new(sourceId, (_, _) => Task.FromResult(OnlineMetadataResult.Success(sourceId, url,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [key] = value })));

    private static OnlineMetadataRequest Request() => new("film", "https://example.test/1", "en");

    [Fact]
    public void KnownHostsAreDistinctAndOrderedByPlugin()
    {
        var manager = new OnlineMetadataExtensionManager(
        [
            Provider("b", Succeeds("b", null, "Title", "B")),
            Provider("a", new StubSource("a", (_, _) => Task.FromResult(OnlineMetadataResult.Failure("a", null, "x")),
                "imdb.com", "wikipedia.org")),
            Provider("c", new StubSource("c", (_, _) => Task.FromResult(OnlineMetadataResult.Failure("c", null, "x")),
                "imdb.com"))
        ]);

        Assert.Equal(["a", "b", "c"], manager.SourceIds);
        Assert.Equal(["imdb.com", "wikipedia.org"], manager.KnownHosts);
        Assert.True(manager.HasSources);
    }

    [Fact]
    public async Task EarlierSourceWinsWhenBothSetTheSameFact()
    {
        var manager = new OnlineMetadataExtensionManager(
        [
            Provider("b", Succeeds("b", "https://b.test/1", "Title", "From B")),
            Provider("a", Succeeds("a", "https://a.test/1", "Title", "From A"))
        ]);

        var run = await manager.FetchAsync(["a", "b"], Request(), LookupTimeout, CancellationToken.None);

        Assert.Equal("From A", run.Facts["Title"]);
        Assert.Collection(run.Results,
            result => Assert.Equal("a", result.SourceId),
            result => Assert.Equal("b", result.SourceId));
    }

    [Fact]
    public async Task AFailingSourceDoesNotBlockTheOthers()
    {
        var bad = new StubSource("bad", (_, _) => throw new HttpRequestException("boom"));
        var manager = new OnlineMetadataExtensionManager(
        [
            Provider("a", bad),
            Provider("b", Succeeds("good", "https://good.test/1", "Title", "Good"))
        ]);

        var run = await manager.FetchAsync(["bad", "good"], Request(), LookupTimeout, CancellationToken.None);

        Assert.Equal("Good", run.Facts["Title"]);
        Assert.Collection(run.Results,
            result => { Assert.Equal("bad", result.SourceId); Assert.False(result.Succeeded); Assert.NotNull(result.Error); },
            result => { Assert.Equal("good", result.SourceId); Assert.True(result.Succeeded); });
    }

    [Fact]
    public async Task ASlowSourceTimesOutWithoutBlockingTheOthers()
    {
        var slow = new StubSource("slow", async (_, token) =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return OnlineMetadataResult.Success("slow", null, new Dictionary<string, string>());
        });
        var manager = new OnlineMetadataExtensionManager(
        [
            Provider("a", slow),
            Provider("b", Succeeds("good", "https://good.test/1", "Title", "Good"))
        ]);

        var run = await manager.FetchAsync(["slow", "good"], Request(), TimeSpan.FromMilliseconds(50),
            CancellationToken.None);

        Assert.Equal("Good", run.Facts["Title"]);
        Assert.False(run.Results[0].Succeeded);
        Assert.True(run.Results[1].Succeeded);
    }

    [Fact]
    public async Task SourcesAreProcessedInTheRequestedOrderOnly()
    {
        var seen = new List<string>();
        var manager = new OnlineMetadataExtensionManager(
        [
            Provider("a", new StubSource("a", (_, _) => { seen.Add("a"); return Task.FromResult(OnlineMetadataResult.Failure("a", null, "x")); })),
            Provider("b", new StubSource("b", (_, _) => { seen.Add("b"); return Task.FromResult(OnlineMetadataResult.Failure("b", null, "x")); }))
        ]);

        await manager.FetchAsync(["b", "a"], Request(), LookupTimeout, CancellationToken.None);

        Assert.Equal(["b", "a"], seen);
    }

    [Fact]
    public async Task UnknownSourceIdIsSkippedWithoutFetching()
    {
        var fetched = false;
        var manager = new OnlineMetadataExtensionManager(
        [
            Provider("a", new StubSource("a", (_, _) =>
            {
                fetched = true;
                return Task.FromResult(OnlineMetadataResult.Failure("a", null, "x"));
            }))
        ]);

        var run = await manager.FetchAsync(["missing"], Request(), LookupTimeout, CancellationToken.None);

        Assert.Empty(run.Results);
        Assert.False(fetched);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var manager = new OnlineMetadataExtensionManager(
        [
            Provider("a", new StubSource("a", async (_, token) =>
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
                return OnlineMetadataResult.Failure("a", null, "x");
            }))
        ]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.FetchAsync(["a"], Request(), LookupTimeout, cancellation.Token));
    }
}
