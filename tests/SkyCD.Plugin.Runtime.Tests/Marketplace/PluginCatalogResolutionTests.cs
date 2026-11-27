using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Runtime.Marketplace.Models;
using SkyCD.Plugin.Runtime.Marketplace.Services;
using SkyCD.Plugin.Runtime.Marketplace.Transport;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests.Marketplace;

/// <summary>End-to-end resolution of the catalog, manifests and NuGet feeds through a deterministic handler.</summary>
public sealed class PluginCatalogResolutionTests
{
    [Fact]
    public async Task LoadsSingleSourceAndSelectsTheHighestCompatibleRelease()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedA, PluginCatalogFixture.FeedABase,
            ("SkyCD.Plugin.Example", ["1.0.0", "2.0.0"]));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.Example",
                PluginCatalogFixture.Release("1.0.0") + ", " + PluginCatalogFixture.Release("2.0.0"))));
        using var _ = catalog;

        var result = await catalog.GetCatalogAsync();

        Assert.Equal(PluginCatalogStatus.Loaded, result.Snapshot.CatalogStatus);
        var source = Assert.Single(result.Snapshot.Sources);
        Assert.Equal(PluginSourceStatus.Loaded, source.Status);
        Assert.Equal(new Uri(PluginCatalogFixture.SourceAUri), source.ManifestUri);
        Assert.Equal("Fixture publisher", source.Publisher);

        var package = Assert.Single(result.Snapshot.Packages);
        Assert.Equal(PluginCatalogFixture.SourceAUri, package.SourceUri.AbsoluteUri);
        Assert.Equal("SkyCD.Plugin.Example", package.PackageId);
        Assert.Equal(new Uri(PluginCatalogFixture.FeedA), package.FeedUri);
        Assert.Equal("2.0.0", package.BestRelease!.Version.Original);
        Assert.Equal(PluginCompatibilityStatus.Compatible, package.BestRelease.Compatibility.Status);
        Assert.Equal(new Uri("https://feed-a.test/api/packages/skycd.plugin.example/2.0.0/skycd.plugin.example.2.0.0.nupkg"),
            package.BestRelease.PackageContentUri);
        Assert.Equal("2.0.0", Assert.Single(result.Snapshot.BestReleases).Version.Original);
        Assert.Empty(result.Snapshot.FailingSources);
    }

    [Fact]
    public async Task RegistrationOnlyFeed_SelectsTheRegisteredPackageContent()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        using var _ = catalog;
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.Example", PluginCatalogFixture.Release("1.0.0"))));
        feed.Serve(PluginCatalogFixture.FeedA, """
            { "resources": [{ "@id": "https://feed-a.test/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }] }
            """);
        const string asset = "https://releases.test/download/v1.0.0/example.nupkg";
        feed.Serve("https://feed-a.test/registration/skycd.plugin.example/index.json", $$"""
            { "items": [{ "items": [{
                "catalogEntry": { "id": "SkyCD.Plugin.Example", "version": "1.0.0" },
                "packageContent": "{{asset}}"
            }] }] }
            """);

        var result = await catalog.GetCatalogAsync();

        Assert.Equal(new Uri(asset), Assert.Single(result.Snapshot.SelectedPackageContentUris));
    }

    [Fact]
    public async Task DiscoversPackageContentResourceThroughTheServiceIndex()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.Serve(PluginCatalogFixture.FeedA, """
            {
              "version": "3.0.0",
              "resources": [
                { "@id": "https://feed-a.test/registration/index.json", "@type": "RegistrationsBaseUrl/3.6.0" },
                { "@id": "https://feed-a.test/content/", "@type": "PackageBaseAddress/3.0.0" }
              ]
            }
            """);
        feed.Serve("https://feed-a.test/content/skycd.plugin.example/index.json", """{ "versions": ["1.2.3"] }""");
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.Example", PluginCatalogFixture.Release("1.2.3"))));
        using var _ = catalog;

        var result = await catalog.GetCatalogAsync();

        var release = Assert.Single(result.Snapshot.Packages).BestRelease!;
        Assert.Equal(PluginCompatibilityStatus.Compatible, release.Compatibility.Status);
        Assert.Contains("https://feed-a.test/content/skycd.plugin.example/index.json", feed.Requests);
        Assert.DoesNotContain("https://feed-a.test/registration/index.json", feed.Requests);
        Assert.Equal(new Uri("https://feed-a.test/content/skycd.plugin.example/1.2.3/skycd.plugin.example.1.2.3.nupkg"),
            release.PackageContentUri);
        Assert.Equal(release.PackageContentUri, Assert.Single(result.Snapshot.SelectedPackageContentUris));
    }

    [Fact]
    public async Task NewerIncompatibleVersionDoesNotHideOlderEligibleVersion()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedA, PluginCatalogFixture.FeedABase,
            ("SkyCD.Plugin.Example", ["1.0.0", "3.0.0"]));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.Example",
                PluginCatalogFixture.Release("1.0.0") + ", " + PluginCatalogFixture.Release("3.0.0", "4.0.0"))));
        using var _ = catalog;

        var package = Assert.Single((await catalog.GetCatalogAsync()).Snapshot.Packages);

        Assert.Equal("1.0.0", package.BestRelease!.Version.Original);
        Assert.Equal(new[] { "3.0.0", "1.0.0" }, package.Releases.Select(static release => release.Version.Original));
        var incompatible = package.Releases[0];
        Assert.Equal(PluginCompatibilityStatus.Incompatible, incompatible.Compatibility.Status);
        Assert.Equal(PluginReleaseIncompatibility.HostTooOld, incompatible.Compatibility.Reason);
        Assert.Contains("4.0.0", incompatible.Compatibility.Message);
    }

    [Fact]
    public async Task NewerPrereleaseDoesNotHideOlderStableVersion()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedA, PluginCatalogFixture.FeedABase,
            ("SkyCD.Plugin.Example", ["1.0.0", "2.0.0-beta.1"]));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.Example",
                PluginCatalogFixture.Release("1.0.0") + ", " + PluginCatalogFixture.Release("2.0.0-beta.1"))));
        using var _ = catalog;

        var package = Assert.Single((await catalog.GetCatalogAsync()).Snapshot.Packages);

        Assert.Equal("1.0.0", package.BestRelease!.Version.Original);
        var prerelease = package.Releases[0];
        Assert.True(prerelease.IsPrerelease);
        Assert.Equal(PluginReleaseIncompatibility.Prerelease, prerelease.Compatibility.Reason);
        Assert.Equal(PluginCompatibilityStatus.Incompatible, prerelease.Compatibility.Status);
    }

    [Fact]
    public async Task PrereleaseIsSelectableWhenPrereleasesAreAllowed()
    {
        var (catalog, feed) = PluginCatalogFixture.Create(options => options with { IncludePrerelease = true });
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedA, PluginCatalogFixture.FeedABase,
            ("SkyCD.Plugin.Example", ["1.0.0", "2.0.0-beta.1"]));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.Example",
                PluginCatalogFixture.Release("1.0.0") + ", " + PluginCatalogFixture.Release("2.0.0-beta.1"))));
        using var _ = catalog;

        var package = Assert.Single((await catalog.GetCatalogAsync()).Snapshot.Packages);

        Assert.Equal("2.0.0-beta.1", package.BestRelease!.Version.Original);
    }

    [Fact]
    public async Task PlatformMismatchIsIncompatibleAndAbsentPlatformsMeansAnyPlatform()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedA, PluginCatalogFixture.FeedABase,
            ("SkyCD.Plugin.Portable", ["1.0.0"]),
            ("SkyCD.Plugin.Linux", ["1.0.0"]),
            ("SkyCD.Plugin.Windows", ["1.0.0"]));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.Portable", PluginCatalogFixture.Release("1.0.0")) + ", " +
            PluginCatalogFixture.Package("SkyCD.Plugin.Linux",
                PluginCatalogFixture.Release("1.0.0", platforms: PluginCatalogFixture.Platforms("linux-x64", "linux-arm64"))) + ", " +
            PluginCatalogFixture.Package("SkyCD.Plugin.Windows",
                PluginCatalogFixture.Release("1.0.0", platforms: PluginCatalogFixture.Platforms("win-x64", "osx-arm64")))));
        using var _ = catalog;

        var packages = (await catalog.GetCatalogAsync()).Snapshot.Packages.ToDictionary(
            static package => package.PackageId, static package => package);

        Assert.NotNull(packages["SkyCD.Plugin.Portable"].BestRelease);
        Assert.NotNull(packages["SkyCD.Plugin.Windows"].BestRelease);
        Assert.Null(packages["SkyCD.Plugin.Linux"].BestRelease);
        Assert.Equal(PluginReleaseIncompatibility.PlatformMismatch,
            packages["SkyCD.Plugin.Linux"].Releases[0].Compatibility.Reason);
    }

    [Fact]
    public async Task ReleaseMissingFromTheFeedIsUnavailable()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedA, PluginCatalogFixture.FeedABase,
            ("SkyCD.Plugin.Example", ["1.0.0"]));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.Example",
                PluginCatalogFixture.Release("1.0.0") + ", " + PluginCatalogFixture.Release("2.0.0"))));
        using var _ = catalog;

        var package = Assert.Single((await catalog.GetCatalogAsync()).Snapshot.Packages);

        Assert.Equal("1.0.0", package.BestRelease!.Version.Original);
        Assert.Equal(PluginReleaseIncompatibility.NotPublished, package.Releases[0].Compatibility.Reason);
        Assert.Equal(PluginCompatibilityStatus.Incompatible, package.Releases[0].Compatibility.Status);
    }

    [Fact]
    public async Task FeedWithoutPackageContentResourceYieldsUnknownCompatibility()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.Serve(PluginCatalogFixture.FeedA, """{ "version": "3.0.0", "resources": [] }""");
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.Example", PluginCatalogFixture.Release("1.0.0"))));
        using var _ = catalog;

        var package = Assert.Single((await catalog.GetCatalogAsync()).Snapshot.Packages);

        Assert.Null(package.BestRelease);
        Assert.Equal(PluginCompatibilityStatus.Unknown, package.Releases[0].Compatibility.Status);
        Assert.StartsWith("Compatibility unknown:", package.Releases[0].Compatibility.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FeedFailureYieldsUnknownCompatibility()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.Fail(PluginCatalogFixture.FeedA, HttpStatusCode.ServiceUnavailable);
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.Example", PluginCatalogFixture.Release("1.0.0"))));
        using var _ = catalog;

        var package = Assert.Single((await catalog.GetCatalogAsync()).Snapshot.Packages);

        Assert.Equal(PluginSourceStatus.Loaded, Assert.Single(catalog.CurrentSnapshot!.Sources).Status);
        Assert.Null(package.BestRelease);
        Assert.Equal(PluginCompatibilityStatus.Unknown, package.Releases[0].Compatibility.Status);
    }

    [Fact]
    public async Task DuplicatePackageIdAcrossFeedsIsBoundToItsSourceWithDeterministicWinner()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl,
            PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceBUri, PluginCatalogFixture.SourceAUri));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedA, PluginCatalogFixture.FeedABase,
            ("SkyCD.Plugin.Shared", ["1.0.0", "1.5.0"]));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedB, PluginCatalogFixture.FeedBBase,
            ("SkyCD.Plugin.Shared", ["1.0.0", "1.5.0"]));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.Shared",
                PluginCatalogFixture.Release("1.0.0") + ", " + PluginCatalogFixture.Release("1.5.0"))));
        feed.Serve(PluginCatalogFixture.SourceBUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedB,
            PluginCatalogFixture.Package("SkyCD.Plugin.Shared",
                PluginCatalogFixture.Release("1.0.0") + ", " + PluginCatalogFixture.Release("1.5.0"))));
        using var _ = catalog;

        var result = await catalog.GetCatalogAsync();
        var copies = result.Snapshot.FindPackages("SkyCD.Plugin.Shared");

        Assert.Equal(2, copies.Count);
        Assert.Equal(new[] { PluginCatalogFixture.SourceAUri, PluginCatalogFixture.SourceBUri },
            copies.Select(static copy => copy.SourceUri.AbsoluteUri));
        foreach (var copy in copies)
        {
            Assert.All(copy.Releases, release => Assert.Equal(copy.SourceUri, release.SourceUri));
        }

        // Equal versions: the winner is chosen by source URL ordinal ("a" < "b").
        var winner = Assert.Single(result.Snapshot.BestReleases);
        Assert.Equal(PluginCatalogFixture.SourceAUri, winner.SourceUri.AbsoluteUri);

        // A higher version on the other feed wins regardless of catalog order.
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedB, PluginCatalogFixture.FeedBBase,
            ("SkyCD.Plugin.Shared", ["1.0.0", "1.5.0", "2.0.0"]));
        feed.Serve(PluginCatalogFixture.SourceBUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedB,
            PluginCatalogFixture.Package("SkyCD.Plugin.Shared",
                PluginCatalogFixture.Release("1.0.0") + ", " + PluginCatalogFixture.Release("1.5.0") + ", " +
                PluginCatalogFixture.Release("2.0.0"))));

        var refreshed = await catalog.RefreshAsync();
        var group = Assert.Single(refreshed.Snapshot.Duplicates);
        Assert.Equal("skycd.plugin.shared", group.PackageId);
        Assert.Equal(PluginCatalogFixture.SourceBUri, group.Winner!.SourceUri.AbsoluteUri);
        Assert.Equal("2.0.0", Assert.Single(refreshed.Snapshot.BestReleases).Version.Original);
    }

    [Fact]
    public async Task DuplicateEntriesInsideOneManifestAreRejected()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedA, PluginCatalogFixture.FeedABase,
            ("SkyCD.Plugin.Example", ["1.0.0"]));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.Example", PluginCatalogFixture.Release("1.0.0")) + ", " +
            PluginCatalogFixture.Package("skycd.plugin.example", PluginCatalogFixture.Release("1.0.0"))));
        using var _ = catalog;

        var result = await catalog.GetCatalogAsync();

        var source = Assert.Single(result.Snapshot.Sources);
        Assert.Equal(PluginSourceStatus.Malformed, source.Status);
        Assert.Empty(source.Packages);
        Assert.Contains(result.Diagnostics, static line => line.Contains("listed twice", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmptyCatalogIsAnEmptyStateNotAnError()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, "[]");
        using var _ = catalog;

        var result = await catalog.GetCatalogAsync();

        Assert.Equal(PluginCatalogStatus.Loaded, result.Snapshot.CatalogStatus);
        Assert.True(result.Snapshot.IsEmpty);
        Assert.Empty(result.Snapshot.Sources);
        Assert.Empty(result.Snapshot.Packages);
        Assert.Empty(result.Diagnostics);
        Assert.False(result.Snapshot.IsOffline);
    }

    [Fact]
    public async Task InvalidCatalogEntriesAreSkippedIndividually()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(
            "http://insecure.test/plugins.json",
            PluginCatalogFixture.SourceAUri,
            PluginCatalogFixture.SourceAUri,
            "not a url",
            "42",
            PluginCatalogFixture.SourceBUri));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedA, PluginCatalogFixture.FeedABase,
            ("SkyCD.Plugin.A", ["1.0.0"]));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedB, PluginCatalogFixture.FeedBBase,
            ("SkyCD.Plugin.B", ["1.0.0"]));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.A", PluginCatalogFixture.Release("1.0.0"))));
        feed.Serve(PluginCatalogFixture.SourceBUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedB,
            PluginCatalogFixture.Package("SkyCD.Plugin.B", PluginCatalogFixture.Release("1.0.0"))));
        using var _ = catalog;

        var result = await catalog.GetCatalogAsync();

        Assert.Equal(PluginCatalogStatus.Loaded, result.Snapshot.CatalogStatus);
        var rejected = result.Snapshot.Sources.Where(static source => source.Status == PluginSourceStatus.Rejected).ToArray();
        Assert.Equal(3, rejected.Length);
        Assert.Equal(new[] { "http://insecure.test/plugins.json", "not a url", "42" },
            rejected.Select(static source => source.RawEntry));
        Assert.DoesNotContain(feed.Requests, static url => url.StartsWith("http:", StringComparison.Ordinal));
        Assert.Equal(2, result.Snapshot.HealthySources.Count);
        Assert.Equal(new[] { "SkyCD.Plugin.A", "SkyCD.Plugin.B" },
            result.Snapshot.Packages.Select(static package => package.PackageId));
        Assert.Contains(result.Diagnostics, static line => line.Contains("appears more than once", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnsupportedManifestSchemaIsReportedSeparately()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(
            PluginCatalogFixture.SourceAUri, PluginCatalogFixture.SourceBUri));
        feed.Serve(PluginCatalogFixture.SourceAUri, """{ "schemaVersion": 2, "packages": "future shape" }""");
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedB, PluginCatalogFixture.FeedBBase,
            ("SkyCD.Plugin.B", ["1.0.0"]));
        feed.Serve(PluginCatalogFixture.SourceBUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedB,
            PluginCatalogFixture.Package("SkyCD.Plugin.B", PluginCatalogFixture.Release("1.0.0"))));
        using var _ = catalog;

        var result = await catalog.GetCatalogAsync();

        var unsupported = result.Snapshot.Sources[0];
        Assert.Equal(PluginSourceStatus.UnsupportedSchema, unsupported.Status);
        Assert.Equal("UnsupportedSchema", unsupported.Detail.Code);
        Assert.Contains("schema version 2", unsupported.Detail.Message, StringComparison.Ordinal);
        Assert.Equal(PluginSourceStatus.Loaded, result.Snapshot.Sources[1].Status);
        Assert.True(result.Snapshot.HasPartialFailure);
        Assert.Single(result.Snapshot.Packages);
    }

    [Fact]
    public async Task MalformedManifestIsReportedSeparatelyFromNetworkFailure()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(
            PluginCatalogFixture.SourceAUri, PluginCatalogFixture.SourceBUri));
        feed.Serve(PluginCatalogFixture.SourceAUri, "{ this is not json");
        feed.Serve(PluginCatalogFixture.SourceBUri, "{}", HttpStatusCode.NotFound);
        using var _ = catalog;

        var result = await catalog.GetCatalogAsync();

        Assert.Equal(PluginSourceStatus.Malformed, result.Snapshot.Sources[0].Status);
        Assert.Equal(PluginSourceStatus.Offline, result.Snapshot.Sources[1].Status);
        Assert.True(result.Snapshot.HasTotalFailure);
        Assert.Empty(result.Snapshot.Packages);
    }

    [Fact]
    public async Task OneFailingSourceKeepsHealthyResultsAndRefreshRecovers()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(
            PluginCatalogFixture.SourceAUri, PluginCatalogFixture.SourceBUri));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedA, PluginCatalogFixture.FeedABase,
            ("SkyCD.Plugin.A", ["1.0.0"]));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.A", PluginCatalogFixture.Release("1.0.0"))));
        feed.Fail(PluginCatalogFixture.SourceBUri);
        using var _ = catalog;

        var first = await catalog.BootstrapAsync();
        var healthy = Assert.Single(first.Snapshot.HealthySources);
        Assert.Equal(PluginCatalogFixture.SourceAUri, healthy.ManifestUri.AbsoluteUri);
        var failing = Assert.Single(first.Snapshot.FailingSources);
        Assert.Equal(PluginSourceStatus.Offline, failing.Status);
        Assert.True(first.Snapshot.HasPartialFailure);
        Assert.Equal("SkyCD.Plugin.A", Assert.Single(first.Snapshot.Packages).PackageId);

        // The same failure stays a partial failure on the next refresh; healthy results are never dropped.
        var second = await catalog.RefreshAsync();
        Assert.True(second.Snapshot.HasPartialFailure);
        Assert.Single(second.Snapshot.Packages);

        // Once the publisher is reachable again, an explicit refresh picks it up without a catalog edit.
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedB, PluginCatalogFixture.FeedBBase,
            ("SkyCD.Plugin.B", ["1.0.0"]));
        feed.Serve(PluginCatalogFixture.SourceBUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedB,
            PluginCatalogFixture.Package("SkyCD.Plugin.B", PluginCatalogFixture.Release("1.0.0"))));

        var recovered = await catalog.RefreshAsync();
        Assert.False(recovered.Snapshot.HasPartialFailure);
        Assert.Equal(2, recovered.Snapshot.HealthySources.Count);
        Assert.Equal(new[] { "SkyCD.Plugin.A", "SkyCD.Plugin.B" },
            recovered.Snapshot.Packages.Select(static package => package.PackageId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task OfflineSourceFallsBackToLastKnownGoodAfterABootstrap()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedA, PluginCatalogFixture.FeedABase,
            ("SkyCD.Plugin.A", ["1.0.0"]));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.A", PluginCatalogFixture.Release("1.0.0"))));
        using var _ = catalog;

        var online = await catalog.BootstrapAsync();
        Assert.Equal(PluginCompatibilityStatus.Compatible,
            Assert.Single(online.Snapshot.Packages).BestRelease!.Compatibility.Status);

        feed.Remove(PluginCatalogFixture.SourceAUri);
        var offline = await catalog.RefreshAsync();

        var source = Assert.Single(offline.Snapshot.Sources);
        Assert.Equal(PluginSourceStatus.Offline, source.Status);
        Assert.True(source.IsStale);
        Assert.True(offline.Snapshot.HasTotalFailure);
        var package = Assert.Single(source.Packages);
        Assert.Equal("SkyCD.Plugin.A", package.PackageId);
        Assert.Equal(PluginCompatibilityStatus.Unknown, package.Releases[0].Compatibility.Status);
        Assert.Null(package.BestRelease);
        Assert.Empty(offline.Snapshot.BestReleases);
    }

    [Fact]
    public async Task OfflineCatalogFallsBackToLastKnownSources()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedA, PluginCatalogFixture.FeedABase,
            ("SkyCD.Plugin.A", ["1.0.0"]));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.A", PluginCatalogFixture.Release("1.0.0"))));
        using var _ = catalog;

        await catalog.BootstrapAsync();
        feed.Remove(PluginCatalogFixture.CatalogUrl);

        var offline = await catalog.RefreshAsync();

        Assert.Equal(PluginCatalogStatus.Offline, offline.Snapshot.CatalogStatus);
        Assert.True(offline.Snapshot.IsOffline);
        var source = Assert.Single(offline.Snapshot.Sources);
        Assert.Equal(PluginSourceStatus.Loaded, source.Status);
        Assert.False(source.IsStale);

        // The manifest is still reachable, so its packages are resolved even though the catalog is not.
        Assert.Equal("SkyCD.Plugin.A", Assert.Single(offline.Snapshot.Packages).PackageId);
    }

    [Fact]
    public async Task OfflineWithoutBootstrapIsDistinguishableFromAnEmptyCatalog()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Fail(PluginCatalogFixture.CatalogUrl);
        using var _ = catalog;

        var result = await catalog.GetCatalogAsync();

        Assert.Equal(PluginCatalogStatus.Offline, result.Snapshot.CatalogStatus);
        Assert.True(result.Snapshot.IsOffline);
        Assert.False(result.Snapshot.IsEmpty);
        Assert.Empty(result.Snapshot.Sources);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task MalformedCatalogDocumentIsNotTreatedAsOfflineRetry()
    {
        var (catalog, feed) = PluginCatalogFixture.Create();
        feed.Serve(PluginCatalogFixture.CatalogUrl, """{ "sources": ["https://publisher-a.test/plugins.json"] }""");
        using var _ = catalog;

        var result = await catalog.GetCatalogAsync();

        Assert.Equal(PluginCatalogStatus.Offline, result.Snapshot.CatalogStatus);
        Assert.Contains(result.Diagnostics, static line => line.Contains("not a JSON array", StringComparison.Ordinal));
        Assert.Empty(result.Snapshot.Sources);
    }

    [Fact]
    public async Task NoConfiguredEndpointIsNotAnError()
    {
        var (catalog, feed) = PluginCatalogFixture.Create(options => options with { CatalogEndpoints = [] });
        using var _ = catalog;

        var result = await catalog.GetCatalogAsync();

        Assert.Equal(PluginCatalogStatus.NotConfigured, result.Snapshot.CatalogStatus);
        Assert.True(result.Snapshot.IsEmpty);
        Assert.Equal(0, feed.RequestCount);
    }

    [Fact]
    public async Task ConcurrentRefreshesAreSerialized()
    {
        var feed = new FakePluginFeed();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedA, PluginCatalogFixture.FeedABase,
            ("SkyCD.Plugin.A", ["1.0.0"]));
        var manifest = PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.A", PluginCatalogFixture.Release("1.0.0")));
        var delay = TimeSpan.FromMilliseconds(40);
        var inFlight = 0;
        var maxInFlight = 0;
        feed.Serve(PluginCatalogFixture.SourceAUri, async _ =>
        {
            var current = Interlocked.Increment(ref inFlight);
            maxInFlight = Math.Max(maxInFlight, current);
            await Task.Delay(delay);
            Interlocked.Decrement(ref inFlight);
            return FakePluginFeed.Text(manifest);
        });
        var options = PluginCatalogFixture.Options(o => o with
        {
            RequestTimeout = TimeSpan.FromSeconds(5),
            MaxSourceConcurrency = 1
        });
        using var catalog = new PluginCatalog(options, new InMemoryPluginCatalogCache(),
            new HttpsPluginSourceFetcher(feed, options.RequestTimeout), PluginCatalogFixture.Clock);

        var started = DateTime.UtcNow;
        var refreshes = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => catalog.RefreshAsync()));
        var elapsed = DateTime.UtcNow - started;

        Assert.Equal(3, refreshes.Length);
        Assert.Equal(1, maxInFlight);
        Assert.True(elapsed >= delay * 3, $"Concurrent refreshes were not serialized ({elapsed}).");
        Assert.All(refreshes, static result => Assert.Single(result.Snapshot.Packages));
        Assert.Single(catalog.CurrentSnapshot!.Packages);
    }

    [Fact]
    public async Task CachedSnapshotIsReusedUntilItExpires()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var feed = new FakePluginFeed();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.ServeNuGetFeed(PluginCatalogFixture.FeedA, PluginCatalogFixture.FeedABase,
            ("SkyCD.Plugin.A", ["1.0.0"]));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.A", PluginCatalogFixture.Release("1.0.0"))));
        var options = PluginCatalogFixture.Options();
        using var catalog = new PluginCatalog(options, new InMemoryPluginCatalogCache(),
            new HttpsPluginSourceFetcher(feed, options.RequestTimeout), clock);

        await catalog.GetCatalogAsync();
        var requests = feed.RequestCount;
        await catalog.GetCatalogAsync();
        Assert.Equal(requests, feed.RequestCount);

        clock.Advance(options.CacheLifetime + TimeSpan.FromSeconds(1));
        await catalog.GetCatalogAsync();
        Assert.True(feed.RequestCount > requests);
    }

    [Fact]
    public async Task UserRepositoriesAreResolvedBesideTheCatalogSources()
    {
        var feed = new FakePluginFeed();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.A", PluginCatalogFixture.Release("1.0.0"))));
        feed.Serve(PluginCatalogFixture.SourceBUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedB,
            PluginCatalogFixture.Package("SkyCD.Plugin.B", PluginCatalogFixture.Release("1.0.0"))));
        var repositories = new PluginRepositorySourceStore();
        repositories.SetSources([new Uri(PluginCatalogFixture.SourceBUri)]);
        using var catalog = PluginCatalogFixture.Create(feed, repositorySources: repositories);

        var result = await catalog.GetCatalogAsync();

        Assert.Equal([PluginCatalogFixture.SourceAUri, PluginCatalogFixture.SourceBUri],
            result.Snapshot.Sources.Select(source => source.ManifestUri.AbsoluteUri));
        Assert.All(result.Snapshot.Sources, source => Assert.Equal(PluginSourceStatus.Loaded, source.Status));
        Assert.Equal(["SkyCD.Plugin.A", "SkyCD.Plugin.B"],
            result.Snapshot.Packages.Select(package => package.PackageId).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, line => line.Contains(PluginCatalogFixture.SourceBUri));
    }

    [Fact]
    public async Task RepositoryAlreadyListedByTheCatalogIsReadOnce()
    {
        var feed = new FakePluginFeed();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.A", PluginCatalogFixture.Release("1.0.0"))));
        var repositories = new PluginRepositorySourceStore();
        repositories.SetSources([new Uri(PluginCatalogFixture.SourceAUri)]);
        using var catalog = PluginCatalogFixture.Create(feed, repositorySources: repositories);

        var result = await catalog.GetCatalogAsync();

        Assert.Single(result.Snapshot.Sources);
        Assert.Single(result.Snapshot.Packages);
        Assert.Equal(1, feed.Requests.Count(request =>
            string.Equals(request, PluginCatalogFixture.SourceAUri, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task UnreachableUserRepositoryIsOfflineWithoutDiscardingCatalogPackages()
    {
        var feed = new FakePluginFeed();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.A", PluginCatalogFixture.Release("1.0.0"))));
        var repositories = new PluginRepositorySourceStore();
        repositories.SetSources([new Uri(PluginCatalogFixture.SourceBUri)]);
        using var catalog = PluginCatalogFixture.Create(feed, repositorySources: repositories);

        var result = await catalog.GetCatalogAsync();

        Assert.Equal(PluginSourceStatus.Loaded, result.Snapshot.Sources[0].Status);
        Assert.Equal(PluginSourceStatus.Offline, result.Snapshot.Sources[1].Status);
        Assert.Equal("SkyCD.Plugin.A", Assert.Single(result.Snapshot.Packages).PackageId);
    }

    [Fact]
    public async Task RemovingARepositoryDropsItsPackagesOnTheNextRead()
    {
        var feed = new FakePluginFeed();
        feed.Serve(PluginCatalogFixture.CatalogUrl, PluginCatalogFixture.Catalog(PluginCatalogFixture.SourceAUri));
        feed.Serve(PluginCatalogFixture.SourceAUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedA,
            PluginCatalogFixture.Package("SkyCD.Plugin.A", PluginCatalogFixture.Release("1.0.0"))));
        feed.Serve(PluginCatalogFixture.SourceBUri, PluginCatalogFixture.Manifest(PluginCatalogFixture.FeedB,
            PluginCatalogFixture.Package("SkyCD.Plugin.B", PluginCatalogFixture.Release("1.0.0"))));
        var repositories = new PluginRepositorySourceStore();
        repositories.SetSources([new Uri(PluginCatalogFixture.SourceBUri)]);
        using var catalog = PluginCatalogFixture.Create(feed, repositorySources: repositories);
        Assert.Equal(2, (await catalog.GetCatalogAsync()).Snapshot.Sources.Count);

        await catalog.UpdateRepositorySourcesAsync([]);
        var result = await catalog.GetCatalogAsync();

        var source = Assert.Single(result.Snapshot.Sources);
        Assert.Equal(PluginCatalogFixture.SourceAUri, source.ManifestUri.AbsoluteUri);
        Assert.Equal("SkyCD.Plugin.A", Assert.Single(result.Snapshot.Packages).PackageId);
    }

    [Fact]
    public void RepositoryStoreKeepsOnlyDistinctHttpsUrls()
    {
        var store = new PluginRepositorySourceStore();

        Assert.True(store.SetSources([
            new Uri("https://a.test/plugins.json"),
            new Uri("https://A.test/plugins.json"),
            new Uri("http://b.test/plugins.json"),
            new Uri("https://user:secret@c.test/plugins.json")
        ]));

        Assert.Equal(["https://a.test/plugins.json"],
            store.Sources.Select(source => source.AbsoluteUri));
        Assert.False(store.SetSources([new Uri("https://a.test/plugins.json")]));
    }
}
