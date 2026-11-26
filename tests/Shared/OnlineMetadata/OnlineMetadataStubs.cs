using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.OnlineMetadata.Sources;

namespace SkyCD.TestSupport;

/// <summary>
/// Reads the recorded HTML bodies the online metadata plugin tests run against. The file is source-linked
/// into every online metadata test project because <see cref="StubFetcher"/> implements a plugin-internal
/// seam, so neither type can live in the shared <c>SkyCD.TestSupport</c> assembly.
/// </summary>
internal static class OnlineMetadataFixtures
{
    /// <summary>Reads a recorded body copied to <c>Fixtures/OnlineMetadata</c> in the test output.</summary>
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "OnlineMetadata", name));
}

/// <summary>A canned web fetcher: it records requested URLs and returns fixed bodies or raises failures.</summary>
internal sealed class StubFetcher : IWebFetcher
{
    private readonly Func<string, string?> handler;

    public StubFetcher(Func<string, string?> handler) => this.handler = handler;

    public StubFetcher(string body) : this(_ => body)
    {
    }

    public List<string> Requested { get; } = [];

    public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        Requested.Add(url);
        cancellationToken.ThrowIfCancellationRequested();
        var body = handler(url);
        if (body is null) throw new HttpRequestException($"stub returned no content for {url}");
        return Task.FromResult(body);
    }
}
