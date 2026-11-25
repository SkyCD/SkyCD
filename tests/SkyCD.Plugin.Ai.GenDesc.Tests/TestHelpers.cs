using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Ai.GenDesc.Tests;

internal static class Fixtures
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "GenDesc", name));
}

/// <summary>
/// A canned host online metadata aggregate: it records each call and returns fixed results from its sources,
/// so GenDesc is tested without any site parser or network.
/// </summary>
internal sealed class StubOnlineMetadataService : IOnlineMetadataService
{
    private readonly Func<string, OnlineMetadataRequest, OnlineMetadataResult> handler;

    public StubOnlineMetadataService(IEnumerable<string> sourceIds,
        Func<string, OnlineMetadataRequest, OnlineMetadataResult> handler, params string[] knownHosts)
    {
        SourceIds = sourceIds.ToArray();
        this.handler = handler;
        KnownHosts = knownHosts;
    }

    public IReadOnlyList<string> SourceIds { get; }

    public IReadOnlyList<string> KnownHosts { get; }

    public List<(IReadOnlyList<string> Sources, OnlineMetadataRequest Request, TimeSpan Timeout)> Calls { get; } = [];

    public Task<OnlineMetadataRun> FetchAsync(IReadOnlyList<string> sourceIds, OnlineMetadataRequest request,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((sourceIds, request, timeout));

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<OnlineMetadataResult>();
        foreach (var sourceId in sourceIds)
        {
            if (!SourceIds.Contains(sourceId, StringComparer.OrdinalIgnoreCase)) continue;
            var result = handler(sourceId, request);
            results.Add(result);
            if (!result.Succeeded) continue;
            foreach (var (key, value) in result.Facts)
                if (!merged.ContainsKey(key)) merged[key] = value;
        }

        return Task.FromResult(new OnlineMetadataRun(merged, results));
    }

    /// <summary>The Wikipedia source with the same facts the fixture page parses into.</summary>
    public static StubOnlineMetadataService Wikipedia() => new(["wikipedia"],
        (sourceId, _) => OnlineMetadataResult.Success(sourceId,
            "https://en.wikipedia.org/wiki/Example_Film", Facts(
                ("Title", "Example Film"),
                ("Plot", "A 1999 feature directed by Jane Doe."),
                ("Image", "https://upload.wikimedia.org/example.png"))),
        "wikipedia.org");

    /// <summary>A source that always fails, so the run carries a failure result and no facts.</summary>
    public static StubOnlineMetadataService AlwaysFailing(string sourceId = "wikipedia") => new([sourceId],
        (id, _) => OnlineMetadataResult.Failure(id, null, "boom"), "wikipedia.org");

    private static IReadOnlyDictionary<string, string> Facts(params (string Key, string Value)[] facts)
    {
        var bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in facts) bag[key] = value;
        return bag;
    }
}
