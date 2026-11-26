# Online metadata sources

Description generators such as GenDesc templates render user-editable text from facts
collected on public web sites. The site lookup itself is not part of the generator: each
site is a separate `Metadata` plugin that implements `IOnlineMetadataSourceCapability`, so
every source can be installed, disabled, replaced and versioned on its own.

## Host aggregate

`IOnlineMetadataSourceCapability` (namespace `SkyCD.SDK.Capabilities.Media`) exposes:

- a stable `SourceId` (`wikipedia`, `imdb`, `anidb`, `ann`, `tvcom`), shared with saved
  generator settings and template variables;
- a `DisplayName` for diagnostics;
- the `KnownHosts` it owns, so a pasted URL can be attributed to the right source;
- `FetchAsync(OnlineMetadataRequest, CancellationToken)`, which resolves the request to a
  document, fetches it and returns either `OnlineMetadataResult.Success` with named facts
  and the fetched URL or `OnlineMetadataResult.Failure` with a sanitized reason. Ordinary
  failures are data, never exceptions; caller cancellation propagates.

`OnlineMetadataExtensionManager` (host) aggregates every enabled source. It merges facts
from the requested sources in priority order with first-source-wins precedence, isolates
each lookup with a per-source timeout, and records per-source outcomes for provenance. A
source that fails or times out contributes no facts and never blocks the others. Discovery
and registration read only assembly metadata and capability properties; no source performs
network access until a generation pass explicitly asks for it.

Online generators receive the aggregate through `IOnlineMetadataConsumer.InitializeOnlineMetadata`,
mirroring the storage and dialog consumer hooks. A generator with no installed sources
reports an unavailable plan and refuses to generate instead of silently producing an empty
description.

## Source plugins

| Plugin | Source id | Hosts | Facts |
| --- | --- | --- | --- |
| `SkyCD.Plugin.Metadata.Wikipedia` | `wikipedia` | `wikipedia.org` | JSON REST summary or action-API extract: title, plot, image, description-as-genre |
| `SkyCD.Plugin.Metadata.Imdb` | `imdb` | `imdb.com` | JSON-LD/Open Graph: title, genre, year, runtime, director, cast, rating, original title |
| `SkyCD.Plugin.Metadata.AniDb` | `anidb` | `anidb.net`, `anidb.info` | meta/JSON-LD plus infobox: type, episodes, runtime, studio, staff, synopsis |
| `SkyCD.Plugin.Metadata.AnimeNewsNetwork` | `ann` | `animenewsnetwork.com` | meta/JSON-LD plus profile table: type, episodes, runtime, studio, staff, synopsis |
| `SkyCD.Plugin.Metadata.TvCom` | `tvcom` | `tv.com`, `cbs.com` | meta/JSON-LD with heading and summary fallbacks: genre, year, episodes, cast |

A pasted URL on an owned host is fetched directly; otherwise the source's search endpoint is
queried with the entry title. HTML parsing uses AngleSharp (MIT), the same parser the
DiskExplorer plugin uses; Wikipedia consumes only JSON. HTML/JSON-LD extraction, the fact
bag and URL matching live in `Plugins/Shared/OnlineMetadata` and are compile-linked into the
plugins, so they are not a runtime dependency and not duplicated as a packaged assembly.

## Consumer settings

GenDesc no longer configures online sources itself. The facts are captured with the entry by
the AI description flow and stored with the catalog entry and the media cache, and the host
implements `IGenDescHostApi` to turn one local file into a `GenDescEntryContext`: its file
name, extension, MIME type and every captured fact. GenDesc templates then choose a template
by kind, MIME type, extension, category and language, so the plugin reads no generation
settings of its own beyond the user template directory.

Because facts are captured before generation, disabling a source plugin changes what the
cache can contain but never affects an `.nfo` or description rendered from facts that are
already stored; generation runs offline from local data.
