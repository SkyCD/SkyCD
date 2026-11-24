using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Ai.GenDesc.Import;
using SkyCD.Plugin.Ai.GenDesc.Models;
using Xunit;

namespace SkyCD.Plugin.Ai.GenDesc.Tests;

public class WebSourceOrchestratorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private sealed class StubImporter(string sourceId, Func<ImportRequest, string?>? resolve = null, Action<string, ImportRequest, DescriptionFacts>? parse = null) : IWebSourceImporter
    {
        public string SourceId { get; } = sourceId;
        public string? ResolveUrl(ImportRequest request) => resolve?.Invoke(request);
        public void Parse(string body, ImportRequest request, DescriptionFacts facts) => parse?.Invoke(body, request, facts);
    }

    private static ImportRequest Request(string url) => new("film", url, "en");

    [Fact]
    public async Task EarlierSourceWinsWhenBothSetTheSameFact()
    {
        var first = new StubImporter("a",
            resolve: _ => "https://a.example/1",
            parse: (_, _, f) => f.Set("Title", "From A"));
        var second = new StubImporter("b",
            resolve: _ => "https://b.example/2",
            parse: (_, _, f) => f.Set("Title", "From B"));
        var orchestrator = new WebSourceOrchestrator(new[] { first, second }, new StubFetcher(_ => "<html></html>"));

        var run = await orchestrator.RunAsync(Request("https://a.example/1"), ["a", "b"], Timeout, CancellationToken.None);

        Assert.Equal("From A", run.Facts.Get("Title"));
        Assert.Contains("https://a.example/1", run.Facts.SourceUrls);
        Assert.Contains("https://b.example/2", run.Facts.SourceUrls);
    }

    [Fact]
    public async Task AFailingSourceDoesNotBlockTheOthers()
    {
        var good = new StubImporter("good",
            resolve: _ => "https://good.example/1",
            parse: (_, _, f) => f.Set("Title", "Good"));
        var bad = new StubImporter("bad", resolve: _ => "https://bad.example/1");
        var fetcher = new StubFetcher(url => url.Contains("bad") ? throw new System.Net.Http.HttpRequestException("boom") : "<html></html>");
        var orchestrator = new WebSourceOrchestrator(new[] { bad, good }, fetcher);

        var run = await orchestrator.RunAsync(Request("https://bad.example/1"), ["bad", "good"], Timeout, CancellationToken.None);

        Assert.Equal("Good", run.Facts.Get("Title"));
        Assert.Collection(run.Results,
            result => { Assert.Equal("bad", result.SourceId); Assert.False(result.Succeeded); },
            result => { Assert.Equal("good", result.SourceId); Assert.True(result.Succeeded); });
    }

    [Fact]
    public async Task SourceWithoutAResolvedUrlIsSkippedWithoutFetching()
    {
        var fetcher = new StubFetcher(_ => "<html></html>");
        var importer = new StubImporter("nope");
        var orchestrator = new WebSourceOrchestrator([importer], fetcher);

        var run = await orchestrator.RunAsync(Request(""), ["nope"], Timeout, CancellationToken.None);

        Assert.Empty(fetcher.Requested);
        Assert.Single(run.Results);
        Assert.False(run.Results[0].Succeeded);
    }

    [Fact]
    public async Task SourcesAreProcessedInTheRequestedOrderOnly()
    {
        var seen = new List<string>();
        var a = new StubImporter("a", resolve: _ => "https://a/1", parse: (_, _, _) => seen.Add("a"));
        var b = new StubImporter("b", resolve: _ => "https://b/1", parse: (_, _, _) => seen.Add("b"));
        var orchestrator = new WebSourceOrchestrator(new[] { a, b }, new StubFetcher(_ => "<html></html>"));

        await orchestrator.RunAsync(Request("https://a/1"), ["b", "a"], Timeout, CancellationToken.None);

        Assert.Equal(["b", "a"], seen);
    }

    [Fact]
    public async Task UnrequestedSourceIsNotRun()
    {
        var a = new StubImporter("a", resolve: _ => "https://a/1", parse: (_, _, _) => { });
        var b = new StubImporter("b", resolve: _ => "https://b/1", parse: (_, _, _) => { });
        var orchestrator = new WebSourceOrchestrator(new[] { a, b }, new StubFetcher(_ => "<html></html>"));

        var run = await orchestrator.RunAsync(Request("https://a/1"), ["a"], Timeout, CancellationToken.None);

        Assert.Single(run.Results);
        Assert.Equal("a", run.Results[0].SourceId);
    }
}
