using System;
using System.Globalization;
using System.Threading.Tasks;
using SkyCD.Plugin.Runtime.Marketplace.Models;
using SkyCD.Plugin.Runtime.Marketplace.Services;
using SkyCD.Plugin.Runtime.Marketplace.Transport;

namespace SkyCD.Plugin.Runtime.Tests.Marketplace;

/// <summary>Fixed URLs and options shared by the marketplace tests.</summary>
internal static class PluginCatalogFixture
{
    public const string CatalogUrl = "https://catalog.test/plugins.json";
    public const string SourceAUri = "https://publisher-a.test/plugins.json";
    public const string SourceBUri = "https://publisher-b.test/plugins.json";
    public const string FeedA = "https://feed-a.test/nuget/v3/index.json";
    public const string FeedABase = "https://feed-a.test/api/packages/";
    public const string FeedB = "https://feed-b.test/nuget/v3/index.json";
    public const string FeedBBase = "https://feed-b.test/api/packages/";

    public static readonly Version Host = new(3, 5, 0);

    public static FixedClock Clock { get; } = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

    public static PluginCatalogOptions Options(Func<PluginCatalogOptions, PluginCatalogOptions>? configure = null)
    {
        var options = new PluginCatalogOptions
        {
            CatalogEndpoints = [new Uri(CatalogUrl)],
            HostVersion = Host,
            RuntimeIdentifier = "win-x64",
            RequestTimeout = TimeSpan.FromMilliseconds(250),
            CacheLifetime = TimeSpan.FromMinutes(10)
        };
        return configure is null ? options : configure(options);
    }

    /// <summary>The service under test, using the default HTTPS fetcher against the fake handler.</summary>
    public static (PluginCatalog Catalog, FakePluginFeed Feed) Create(
        Func<PluginCatalogOptions, PluginCatalogOptions>? configure = null)
    {
        var feed = new FakePluginFeed();
        return (Create(feed, configure), feed);
    }

    public static PluginCatalog Create(FakePluginFeed feed,
        Func<PluginCatalogOptions, PluginCatalogOptions>? configure = null, IPluginCatalogCache? cache = null)
    {
        var options = Options(configure);
        var fetcher = new HttpsPluginSourceFetcher(feed, options.RequestTimeout);
        return new PluginCatalog(options, cache ?? new InMemoryPluginCatalogCache(), fetcher, Clock);
    }

    public static string Catalog(params string[] entries)
    {
        return "[" + string.Join(", ", Array.ConvertAll(entries,
            static entry => entry.StartsWith('{') ? entry : "\"" + entry + "\"")) + "]";
    }

    public static string Manifest(string feed, string packages)
    {
        return $$"""
            {
              "schemaVersion": 1,
              "publisher": "Fixture publisher",
              "projectUrl": "https://publisher.test/",
              "feed": "{{feed}}",
              "packages": [ {{packages}} ]
            }
            """;
    }

    public static string Package(string id, string releases,
        string extra = "\"displayName\": \"Fixture package\", \"summary\": \"Summary\", \"license\": \"MIT\"")
    {
        return $$"""{ "id": "{{id}}", {{extra}}, "releases": [ {{releases}} ] }""";
    }

    public static string Release(string version, string minHost = "3.0.0", string? maxHost = null,
        string? platforms = null)
    {
        var max = maxHost is null ? string.Empty : $", \"maxHostVersion\": \"{maxHost}\"";
        var platformText = platforms is null ? string.Empty : $", \"platforms\": [{platforms}]";
        return $$"""{ "version": "{{version}}", "minHostVersion": "{{minHost}}"{{max}}{{platformText}} }""";
    }

    public static string Platforms(params string[] values)
    {
        return string.Join(", ", Array.ConvertAll(values, static value => "\"" + value + "\""));
    }
}

/// <summary>Manual clock so cache lifetimes and request budgets are deterministic.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = now;

    public override DateTimeOffset GetUtcNow()
    {
        return Now;
    }

    public void Advance(TimeSpan delta)
    {
        Now += delta;
    }

    public override string ToString()
    {
        return Now.ToString("O", CultureInfo.InvariantCulture);
    }
}

internal sealed class StaticFetcher(Func<Uri, string> respond) : IPluginSourceFetcher
{
    public Task<string> GetStringAsync(Uri address, long maxBytes, System.Threading.CancellationToken cancellationToken)
    {
        return Task.FromResult(respond(address));
    }
}
