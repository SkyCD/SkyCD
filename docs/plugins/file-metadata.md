# Operating-system file metadata plugin

Implements #728 on the media capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Metadata.FileMetadata/` → plugin id `SkyCD.Plugin.Metadata.FileMetadata`.
Tests: `tests/SkyCD.Plugin.Metadata.FileMetadata.Tests/`.

## What it provides

One capability, discovered from assembly metadata without `plugin.json`:

| Capability | Type | Schema |
|---|---|---|
| OS-assigned tags, comment and rating | `OsMetadataPlugin : ILocalMediaMetadataCapability` | `skycd.file.os-metadata` v1 |

There is no configuration window and no settings capability: the reader always runs, the host has
nothing to configure, and the plugin's settings revision stays empty.

The plugin claims every source (`*`). These values are not in the file bytes; they are stored by the
operating system next to or inside the file, and are what a user or a colleague typed into file
properties rather than exported by an editor. Tags, comments and ratings that tools write *into* an
image, TIFF/RAW container or XMP sidecar are read by the plugins that claim those formats
(`SkyCD.Plugin.Metadata.Image`, `SkyCD.Plugin.Metadata.Xmp`), so every carrier is claimed exactly once.

## Fields

| Key | Type | Role | Meaning |
|---|---|---|---|
| `Tags` | Text | Tags | Keywords joined with `; `; **omitted** when absent |
| `Comment` | Text | – | Comment or caption from the file's properties; **omitted** when absent |
| `Rating` | Integer | – | 1–5 stars; **omitted** when absent or rejected |

Tags carry the `Tags` role, so the host assigns them as catalog tags through the existing
media-cache effects.

## Platform sources

| Platform | Source | Fields |
|---|---|---|
| Windows | Windows Property System (`SHGetPropertyStoreFromParsingName`, read-only, `GPS_DEFAULT \| GPS_BESTEFFORT`) | `System.Keywords` → tags, `System.Comment` → comment, `System.Rating` (1–99 Shell scale) → rating |
| Linux | `getxattr`: `user.xdg.tags`, `user.xdg.comment` (KDE Dolphin and XDG-aware managers) | tags split on `;`/`,`/newline, comment |
| macOS | `getxattr`: `com.apple.metadata:_kMDItemUserTags` and `com.apple.metadata:kMDItemFinderComment` (binary property lists) | Finder tags (colour suffix after the line break stripped), Finder comment |

Ratings use the Shell's own star ranges (1–12 → 1★, 13–37 → 2★, 38–62 → 3★, 63–87 → 4★, 88–99 →
5★), so the 25 Explorer writes for two stars reads as two. Values of 1–5 are treated as stars
already; zero, negative ("rejected") and absent values produce no rating.

On Windows the reader opens the property store read-only and never writes back. A missing or
broken property handler yields no values rather than a failure; `GPS_BESTEFFORT` is what makes the
store openable for file types whose handler is not installed.

## Missing paths

The class implements `ILocalMediaMetadataCapability`, so the host passes a validated local path
instead of a stream. Without a path — for example during a pure indexing pass over cached facts,
or when the media is offline — the provider is **skipped** rather than run. Reporting a failure
would wrongly downgrade values already captured for the entry, so the host must not see one. The
path is never stored by the plugin; the host supplies it per call and leaves it out of the cached
record.

## Host integration

- Priority 5, below the embedded readers of the image and XMP plugins, so when both publish a field
  the embedded value wins. Each result is still stored separately under its own schema id.
- `MediaProviderSelection.StandardMetadata` and `OptInMetadata` select it as a metadata provider;
  whole-file hashing and preview passes do not.

## Resource behavior

- Extended attributes are bounded before they are read: 64 KiB per attribute on Linux, 256 KiB on
  macOS.
- Damaged property lists and unreadable attributes yield no values for the affected field rather
  than a provider failure, so one broken entry never stops an index pass. Cancellation propagates
  normally.
- The reader never writes to the source and never opens the file itself into memory.

## Dependencies

The reader is pure BCL and P/Invoke; the project adds no NuGet packages and no Avalonia reference.
General metadata libraries were rejected in earlier iterations for license reasons (the usual XMP
toolkit's NuGet license is not an SPDX expression), so the small set of portable values is read
directly. The shared `MetadataScan`/`MetadataValues` helpers are compiled from
`Plugins/Shared/MediaMetadata/`; container parsers stay with the plugins that claim those
containers.

## Tests

`tests/SkyCD.Plugin.Metadata.FileMetadata.Tests/` covers:

- descriptors: every source claimed, priority below the embedded readers;
- missing paths, files without properties and unreadable stores yielding no values or a silent
  skip instead of a failure; progress completion; cancellation; null context rejected;
- Windows `PROPVARIANT` decoding for scalar strings, keyword vectors and rating clamping;
- Linux `user.xdg.*` and macOS Finder attributes written with `setxattr` and read back on those
  systems;
- binary property-list decoding, including Finder colour suffixes and corrupt trailers;
- the host pipeline: `*` claims, no stream read for a path-addressed provider, silent skip without
  a path, and discovery of the single capability without a manifest or configuration surface.
