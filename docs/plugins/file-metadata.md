# File metadata plugin

Implements #728 on the media capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.FileMetadata/` → plugin id `SkyCD.Plugin.FileMetadata`.
Tests: `tests/SkyCD.Plugin.FileMetadata.Tests/`.

## What it provides

Two capabilities in one assembly, discovered from assembly metadata without `plugin.json`:

| Capability | Type | Schema |
|---|---|---|
| Portable file metadata | `FileMetadataPlugin : IMediaMetadataCapability` | `skycd.file.metadata` v1 |
| Options surface | `FileMetadataConfigurationPlugin : IPluginConfigurationCapability` | `file-metadata.settings` |

The metadata provider claims `.jpg`, `.jpeg`, `.png`, `.tif`, `.tiff`, `.webp`, `.xmp` and the
TIFF-based RAW extensions `.dng`, `.cr2`, `.nef`, `.arw`. MIME matching also accepts
`image/jpeg`, `image/png`, `image/tiff` and `image/webp`. The extension list is configurable.

## Metadata schema

| Key | Type | Role | Meaning |
|---|---|---|---|
| `Keywords` | Text | Tags | Flat keywords joined with `; ` |
| `HierarchicalKeywords` | Text | Tags | Hierarchical paths, optionally split per level |
| `Comment` | Text | – | Caption/comment, at most 2000 characters; **omitted** when absent |
| `Rating` | Integer | – | 0–5 stars, normalized from the 0–99 scale; **omitted** when absent |

Both tag fields carry the `Tags` role, so the host assigns them as catalog tags through the
existing media-cache effects. Values the plugin did not publish never produce an effect, which is
why each field has its own option.

## Where the values come from

The reader is chosen by magic bytes, not by extension, because tools happily write an XMP sidecar
next to a RAW file or keep an ancient TIFF header under a `.jpg` name.

| Container | Carriers |
|---|---|
| JPEG | APP1 Exif TIFF, APP1 XMP, APP13 Photoshop/IPTC-IIM, COM comment |
| PNG | `tEXt` keywords/comment/description, uncompressed `iTXt` XMP and text, `eXIf` TIFF |
| WebP | `XMP ` and `EXIF` chunks |
| TIFF and TIFF-based RAW | IFD0 tags 270/0x9C9C/0x9C9E/0x9286/0x4746/0x4749, tag 700 XMP, tag 33723 IPTC |
| XMP sidecar | The packet itself |

Properties read from XMP:

- `dc:subject` → keywords
- `pdf:Keywords` → keywords, split on `;` and `,`
- `lr:hierarchicalSubject` → hierarchical paths
- `dc:description` (`x-default` preferred) or `exif:UserComment` → comment
- `xmp:Rating` (0–5) or `MicrosoftPhoto:Rating` (0–99) → rating; negative values mean "rejected"
  and are ignored

IPTC-IIM reads dataset 2:25 (keywords) and 2:120 (caption). TIFF reads `ImageDescription`,
`XPComment`, `XPKeywords`, `UserComment`, `Rating`, `RatingPercent`, IPTC (33723) and XMP (700).
The `XP*` tags are UCS-2 little-endian even inside a big-endian directory, and the reader follows
that.

## Hierarchical tags

`lr:hierarchicalSubject` stores paths such as `Places|Beach|Sunset`.

- **Split enabled (default)** publishes cumulative levels: `Places`, `Places / Beach`,
  `Places / Beach / Sunset`. Filtering on an upper level still finds the item, and the leaf keeps
  its full context in one tag because the host only splits tag lists on `;` and `,`.
- **Split disabled** publishes the path exactly as stored (`Places|Beach|Sunset`) as a single tag,
  which is what catalog tools that round-trip the raw value expect.

## Options

| Key | Default | Effect |
|---|---|---|
| `collectionEnabled` | `true` | Master switch; when off the provider claims no sources at all |
| `keywordsEnabled` | `true` | Publishes both tag fields |
| `splitHierarchicalTags` | `true` | Applies the behavior above |
| `commentsEnabled` | `true` | Publishes `Comment` |
| `ratingsEnabled` | `true` | Publishes `Rating` |
| `extensions` | see above | Semicolon-separated extension list |

Changing any option changes the provider's settings revision, so cached results are invalidated
and re-extracted.

## Platform and file-manager support

The contract is portable metadata **stored inside the file**. That covers the tools that write
standards rather than private sidecars:

| Platform / tool | Supported | Notes |
|---|---|---|
| Windows Explorer (JPEG/TIFF) | Yes | Tags, comments and 1–5 stars persist as XMP/IPTC in the file; 0–99 ratings are converted |
| Windows Explorer (other formats) | No | Shell properties for e.g. Office or MP3 live in property handlers or alternative streams, not in portable tags |
| Lightroom, Bridge, digiKam, Photo Mechanic, XnView | Yes | XMP and IPTC, including hierarchical keywords and `.xmp` sidecars |
| macOS Finder tags | No | Stored in the `com.apple.metadata:_kMDItemUserTags` extended attribute, not in the file bytes |
| macOS comments on JPEG/TIFF | Yes | Finder writes them through the same XMP/IPTC fields |
| Linux gvfs / Tracker / KDE metadata | No | Stored in user-level databases keyed by path, not in the file |
| Windows "Details" on TIFF | Yes | `XPKeywords`/`XPComment`/`Rating` tags |

Extended attributes and sidecar databases are out of reach by design: the host hands plugins a
path-less `IMediaSource`, so a plugin cannot ask the OS for per-path metadata. Reading an `.xmp`
file as its own catalog entry works, because the sidecar is then the file.

## Resource behavior

- The plugin reads at most a 1 MiB prefix (`FileMetadataOptions.MaxScanBytes`) in 256 KiB chunks.
  All supported carriers sit in header segments and early chunks; the cap keeps a scan cheap even
  on multi-gigabyte RAW files and stays far below the host's 16 MiB source budget.
- The XMP parser prohibits DTDs, resolves no external entities and ignores processing instructions
  beyond the packet wrapper. A hostile packet can waste at most one bounded parse.
- Damaged containers, malformed XML, invalid offsets and unsupported text encodings yield no
  values for the affected field rather than a provider failure, so one broken file never stops an
  index pass. Cancellation propagates normally.
- The plugin never writes to the source. No file is opened into memory whole, and no sidecar is
  created.

## Dependencies

The plugin adds no NuGet packages for extraction. The obvious general metadata library pulls in an
XMP toolkit whose NuGet package carries no license expression — the policy gate classifies it as
unknown — so the few portable tags needed here are parsed directly, the same decision
`SkyCD.Plugin.Image` documented for EXIF. Avalonia is referenced only for the options window and
is already used by other plugins; extraction never touches UI types.

## Tests

`tests/SkyCD.Plugin.FileMetadata.Tests/` builds byte-accurate JPEG, PNG, TIFF (both byte orders)
and XMP fixtures in memory and covers:

- every carrier: JPEG XMP/IPTC/COM, PNG text and XMP chunks, TIFF XP tags and rating scales, XMP
  sidecars, Microsoft rating conversion, rejected ratings;
- the split option in both states and each per-field option;
- malformed, truncated, empty and bounded-beyond-prefix sources;
- cancellation and progress completion;
- the host pipeline: extension matching, settings revisions, the scheduler storing a record the
  catalog turns into tags, and discovery without a manifest.
