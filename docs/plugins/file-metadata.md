# File metadata plugin

Implements #728 on the media capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.FileMetadata/` → plugin id `SkyCD.Plugin.FileMetadata`.
Tests: `tests/SkyCD.Plugin.FileMetadata.Tests/`.

## What it provides

Two capabilities in one assembly, discovered from assembly metadata without `plugin.json`:

| Capability | Type | Schema |
|---|---|---|
| Tags inside non-image containers | `FileMetadataPlugin : IMediaMetadataCapability` | `skycd.file.metadata` v1 |
| OS-assigned tags, comment and rating | `OsMetadataPlugin : ILocalMediaMetadataCapability` | `skycd.file.os-metadata` v1 |

There is no configuration window and no settings capability: both readers always run, the host has
nothing to configure, and a plugin's settings revision stays empty.

The image formats the image plugin presents (JPEG, PNG, WebP, GIF) are deliberately **not** claimed
here. Their embedded tags are read by `SkyCD.Plugin.Image` from the same bytes it already decodes
(see `docs/plugins/image.md`), so every carrier is claimed exactly once.

## Embedded metadata (`skycd.file.metadata`)

Claims `.tif`, `.tiff`, `.dng`, `.cr2`, `.nef`, `.arw` and `.xmp`, plus MIME types `image/tiff` and
`application/rdf+xml`. The reader is chosen by magic bytes, not by extension, because tools store a
TIFF payload under a RAW name and an XMP packet in a file named after something else.

| Key | Type | Role | Meaning |
|---|---|---|---|
| `Tags` | Text | Tags | Keywords joined with `; `; **omitted** when absent |
| `Comment` | Text | – | Caption/comment, at most 2000 characters; **omitted** when absent |
| `Rating` | Integer | – | 1–5 stars; **omitted** when absent or rejected |

Tags carry the `Tags` role, so the host assigns them as catalog tags through the existing
media-cache effects. There is no hierarchical-keyword expansion: `lr:hierarchicalSubject` is not
read, so a tool's `Places|Beach|Sunset` path is not turned into synthetic levels.

### Carriers

| Container | Carriers |
|---|---|
| TIFF, DNG, CR2, NEF, ARW | IFD0 tags `ImageDescription` (270), `XPComment` (0x9C9C), `XPKeywords` (0x9C9E), Exif `UserComment` (0x9286), `Rating` (0x4746), `RatingPercent` (0x4749), XMP (700), IPTC-IIM (33723) |
| XMP sidecar | The packet itself |

The `XP*` tags are UCS-2 little-endian even inside a big-endian directory, and the reader follows
that. TIFF `Rating` is the Windows 1–99 scale, normalized with the Shell's own ranges (1–12 → 1★,
13–37 → 2★, 38–62 → 3★, 63–87 → 4★, 88–99 → 5★), so the 25 Explorer writes for two stars reads as
two, not one. Values of 1–5 are treated as stars already.

Properties read from XMP:

- `dc:subject` → tags
- `pdf:Keywords` → tags, split on `;` and `,`
- `dc:description` (`x-default` preferred) or `exif:UserComment` → comment
- `xmp:Rating` (0–5) or `MicrosoftPhoto:Rating` (0–99) → rating; negative values mean "rejected"
  and are ignored

IPTC-IIM reads dataset 2:25 (keywords) and 2:120 (caption).

## OS-assigned metadata (`skycd.file.os-metadata`)

Claims every source (`*`). These values are not in the file bytes; they are stored by the operating
system next to or inside the file, and are what a user or a colleague typed into file properties
rather than exported by an editor. The class implements `ILocalMediaMetadataCapability`, so the
host passes a validated local path instead of a stream.

| Platform | Source | Fields |
|---|---|---|
| Windows | Windows Property System (`SHGetPropertyStoreFromParsingName`, read-only, `GPS_BESTEFFORT`) | `System.Keywords` → tags, `System.Comment` → comment, `System.Rating` (1–99 Shell scale) → rating |
| Linux | `getxattr`: `user.xdg.tags`, `user.xdg.comment` (KDE Dolphin and XDG-aware managers) | tags split on `;`/`,`/newline, comment |
| macOS | `getxattr`: `com.apple.metadata:_kMDItemUserTags` and `com.apple.metadata:kMDItemFinderComment` (binary property lists) | Finder tags (colour suffix after the line break stripped), Finder comment |

On Windows the reader opens the property store read-only and never writes back. A missing or
broken property handler yields no values rather than a failure; `GPS_BESTEFFORT` is what makes the
store openable for file types whose handler is not installed.

Without a path — for example during a pure indexing pass over cached facts, or when the media is
offline — the provider is **skipped** rather than run. Reporting a failure would wrongly downgrade
values already captured for the entry, so the host must not see one.

## Host integration

- Priority 10 for the embedded reader, 5 for the OS reader, so when both publish a field the
  embedded value wins. Each result is still stored separately under its own schema id.
- `MediaProviderSelection.StandardMetadata` and `OptInMetadata` select both kinds as metadata
  providers; whole-file hashing and preview passes do not.
- The path is never stored by the plugin; the host supplies it per call and does not include it in
  the cached record.

## Resource behavior

- The embedded reader reads at most a 1 MiB prefix in 256 KiB chunks. All supported carriers sit in
  header directories and early regions, so the cap keeps a scan cheap even on multi-gigabyte RAW
  files and stays far below the host's 16 MiB source budget.
- Extended attributes are bounded before they are read: 64 KiB per attribute on Linux, 256 KiB on
  macOS.
- Damaged containers, malformed XML, invalid offsets, damaged property lists and unreadable
  attributes yield no values for the affected field rather than a provider failure, so one broken
  file never stops an index pass. Cancellation propagates normally.
- Neither reader writes to the source. No sidecar is created, and no file is opened into memory
  whole.

## Dependencies

Both readers are pure BCL and P/Invoke; the project adds no NuGet packages and no Avalonia
reference. General metadata libraries were rejected in earlier iterations for license reasons (the
usual XMP toolkit's NuGet license is not an SPDX expression), so the small set of portable tags is
parsed directly. The XMP/IPTC/TIFF parsers are shared source files in
`Plugins/Shared/MediaMetadata/`, compiled into both this plugin and `SkyCD.Plugin.Image`, which is
how the image plugin reads the same carriers without a duplication of the parsing code.

## Tests

`tests/SkyCD.Plugin.FileMetadata.Tests/` builds byte-accurate TIFF (both byte orders), IPTC, XMP
and binary property-list fixtures in memory and covers:

- every embedded carrier: TIFF `XP*` tags, `ImageDescription`, `UserComment`, both rating tags with
  the Shell ranges, XMP (700) and IPTC (33723), and XMP sidecars;
- magic-byte container detection, malformed maps, absurd entry counts, empty and bounded-beyond-
  prefix sources, cancellation and progress completion;
- binary property-list decoding, including Finder colour suffixes and corrupt trailers;
- OS metadata: missing paths and files without properties; Windows `PROPVARIANT` decoding for
  scalar strings, keyword vectors and rating clamping; Linux `user.xdg.*` attributes and macOS
  Finder attributes written with `setxattr` on those systems;
- the host pipeline: which sources each capability claims, `sourcePath` selection, silent skip
  without a path, the scheduler storing a record the catalog turns into tags, and discovery of both
  capabilities without a manifest.
