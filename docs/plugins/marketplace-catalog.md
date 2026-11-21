# Plugin Marketplace: Catalog and Publisher Manifest

## Scope
This document defines the two remote documents `SkyCD.Plugin.Runtime.Marketplace` reads before anything is
downloaded: the URL-only catalog published by the `awesome-skycd` directory, and the versioned publisher
manifest that each catalog entry points to. Package installation itself is documented with the installer
(issue #803).

Nothing in this flow executes plugin code or package scripts, and no package ID, version history or binary
is committed to the directory.

## Catalog document
Default endpoint:
`https://raw.githubusercontent.com/SkyCD/awesome-skycd/main/plugins.json`

The payload is a JSON array of unique, absolute HTTPS URLs. Each URL points directly to one publisher
manifest. An empty array is valid and means "no published sources".

```json
[
  "https://example.org/skycd/plugins.json",
  "https://plugins.example.net/skycd/index.json"
]
```

Handling rules:
- Endpoints are tried in configured order; the first document that answers is used.
- A non-string or non-HTTPS entry is kept as a rejected source placeholder and does not invalidate the
  catalog.
- An exact duplicate URL is skipped with a diagnostic; the first occurrence wins.
- A malformed catalog document is reported as a catalog failure, not as an empty catalog.
- Delisting a source removes it from browse results. It does not uninstall the plugin from a user's machine.

## Publisher manifest (schema version 1)

```json
{
  "schemaVersion": 1,
  "publisher": "Example publisher",
  "projectUrl": "https://example.org/skycd/",
  "feed": "https://example.org/nuget/v3/index.json",
  "packages": [
    {
      "id": "Example.SkyCD.Plugin.Foo",
      "displayName": "Foo format",
      "summary": "Reads Foo images.",
      "license": "MIT",
      "projectUrl": "https://example.org/skycd/foo",
      "releases": [
        { "version": "1.2.0", "minHostVersion": "3.0.0" },
        { "version": "2.0.0", "minHostVersion": "3.0.0", "maxHostVersion": "3.9.99",
          "platforms": ["win-x64", "linux-x64"] }
      ]
    }
  ]
}
```

| Field | Required | Meaning |
| --- | --- | --- |
| `schemaVersion` | yes | Integer. This client supports `1` only. |
| `feed` | yes | Absolute HTTPS URL of the publisher's NuGet v3 service index. |
| `publisher` | no | Display name shown next to the source. |
| `projectUrl` | no | Absolute HTTPS URL of the publisher's site. |
| `packages[].id` | yes | NuGet package ID, published exactly as written here. |
| `packages[].displayName` / `summary` / `license` / `projectUrl` | no | Display metadata; `license` is an SPDX expression or name. |
| `packages[].releases[].version` | yes | NuGet package version (`MAJOR.MINOR.PATCH[-prerelease][+metadata]`). |
| `packages[].releases[].minHostVersion` | yes | Lowest SkyCD host version that can load the plugin. |
| `packages[].releases[].maxHostVersion` | no | Highest supported host version; must not be below the minimum. |
| `packages[].releases[].platforms` | no | Runtime identifiers or platform prefixes (`win`, `linux`, `osx`, or `win-x64`). Empty means any platform. |

Validation rules:
- The document is accepted whole or rejected whole; one invalid release never silently disappears.
- Versions are ordered with NuGet rules (four numeric segments, SemVer 2 prerelease labels, build metadata
  ignored for ordering and identity). Duplicate package IDs or duplicate versions are rejected.
- Bounds: at most 500 packages per manifest and 100 releases per package.
- An unsupported `schemaVersion` is reported as `UnsupportedSchema`; a newer schema is not treated as
  malformed data.

### Publishing
1. Publish immutable package versions to a public NuGet v3 feed. Never replace a published version; ship a
   new version instead.
2. Serve the manifest from a stable HTTPS URL on the publisher's own domain.
3. Submit that URL once to `awesome-skycd`. New releases then appear without any change to the directory.

## Package feed resolution
The manifest's `feed` is a NuGet v3 service index. The client looks up the `PackageBaseAddress/3.0.0`
resource in it and reads the version list `{base}/{id-lower}/index.json`. From that resource the package
content URL is built:

```
{PackageBaseAddress}/{id-lower}/{version-lower}/{id-lower}.{version-lower}.nupkg
```

A version that the feed does not list is reported as `NotPublished` and cannot be installed. If the feed
cannot be read, or exposes no package content resource, compatibility stays `Unknown` for that refresh.

## Compatibility and version selection
A release is eligible only when every rule passes, in this order:

1. **Host range** – `HostVersion >= minHostVersion` and, when present, `HostVersion <= maxHostVersion`.
2. **Platform** – the release lists the running runtime identifier or its platform prefix, or lists none.
3. **Feed availability** – the feed version list contains the release version.
4. **Prerelease** – prereleases are compatible but are not selected automatically unless
   `PluginCatalogOptions.IncludePrerelease` is set.

Selection picks the highest eligible version per package. A newer incompatible or prerelease version never
hides an older eligible one. Releases that fail a later rule keep the specific reason of the first failing
rule; `Unknown` releases are shown explicitly and are never selected automatically.

## Failure states and last known good
Snapshots distinguish these situations instead of collapsing them into an empty list:

| Surface | Values |
| --- | --- |
| `CatalogStatus` | `Loaded`, `NotConfigured`, `Offline` |
| `PluginSourceStatus` | `Loaded`, `Offline`, `UnsupportedSchema`, `Malformed`, `Rejected` |
| `PluginCatalogSnapshot` | `IsEmpty`, `IsOffline`, `HasPartialFailure`, `HasTotalFailure`, `FailingSources`, `HealthySources` |

`BootstrapAsync` performs the first resolution and records the last known good catalog and manifests.
`RefreshAsync` always goes to the network; a failing source falls back to its cached result and is reported
as `Offline` with `IsStale = true`, while healthy sources keep their fresh results. `GetCatalogAsync`
returns the published snapshot and refreshes it when it expires. `ResetLastKnownGoodAsync` forgets the cached
state; an ordinary refresh never does.

## Bounds and security
- Only absolute HTTPS URLs without user information are accepted; redirects are followed manually, at most
  five hops, and every hop must stay HTTPS.
- Anonymous requests only; no credentials are sent to publishers or feeds.
- Response sizes are enforced while streaming: catalog 1 MiB, manifest 512 KiB, service index and version
  list 256 KiB.
- Every request uses `PluginCatalogOptions.RequestTimeout`; catalog endpoints and manifests are resolved
  concurrently with a bound (`MaxSourceConcurrency`).
- Malformed documents, duplicate entries and unavailable sources produce diagnostics; they never throw out
  of a refresh.

## Configuration
| Option | Default | Purpose |
| --- | --- | --- |
| `CatalogEndpoints` | awesome-skycd `plugins.json` | Catalog documents, tried in order. |
| `HostVersion` / `RuntimeIdentifier` | host assembly version / current process RID | Compatibility inputs. |
| `IncludePrerelease` | `false` | Allow automatic prerelease selection. |
| `CacheLifetime` | 6 hours | When the published snapshot is refreshed again. |
| `RequestTimeout` | 20 s | Per-request timeout. |
| `MaxCatalogBytes`, `MaxManifestBytes`, `MaxFeedDocumentBytes` | 1 MiB / 512 KiB / 256 KiB | Streaming size limits. |
| `MaxSourceConcurrency` | 4 | Manifest requests in flight. |
| `MaxPackagesPerManifest`, `MaxReleasesPerPackage` | 500 / 100 | Document shape limits. |

## NuGet client library decision
`NuGet.Protocol` (Apache-2.0, compatible with `docs/legal/dependency-license-policy.json`) was evaluated and
not adopted:

- The client reads exactly two read-only documents (the service index and one version list) per package. A
  small `HttpClient` + `System.Text.Json` reader covers them without a new dependency tree
  (`NuGet.Protocol` pulls `NuGet.Versioning`, `NuGet.Packaging`, `NuGet.Configuration` and `NuGet.Common`
  plus their transitive packages).
- `NuGet.Protocol` adds credential-provider, plugin and HTTP cache plumbing the catalog must not use.
- Its version comparers accept legacy version syntax that the v3 package content surface does not publish;
  the catalog validates the SemVer subset up front so a bad manifest fails deterministically instead of
  resolving to a version the feed never served.

The custom reader also keeps the HTTPS-only, redirect, size and timeout rules above under our control.

## Tests
`tests/SkyCD.Plugin.Runtime.Tests/Marketplace` runs the full resolution against a deterministic in-memory
HTTP handler (`FakePluginFeed`) with a manual clock: catalog and manifest documents, NuGet service indexes
and version lists are all served from fixtures, so tests cover duplicate package IDs across feeds,
prereleases, version ordering, offline fallback and malformed documents without touching the network.
