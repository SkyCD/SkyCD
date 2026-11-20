# Font metadata and preview plugin

Implements #731 on the capability contracts from `docs/plugins/media-capabilities.md` and #438
(`docs/plugins/image.md`).

Assembly: `Plugins/SkyCD.Plugin.Fonts/` → plugin id `SkyCD.Plugin.Fonts`.
Tests: `tests/SkyCD.Plugin.Fonts.Tests/`.

## What it provides

Three independently discovered capabilities in one assembly:

| Capability | Type | Schema |
|---|---|---|
| Font name, version and copyright | `FontMetadataPlugin : IMediaMetadataCapability` | `skycd.font.metadata` v1 |
| Bounded preview image rendered with the font's glyphs | `FontPreviewPlugin : IMediaPreviewCapability` | `skycd.font.preview` v1 |
| Configuration surface | `FontConfigurationPlugin : IPluginConfigurationCapability` | – |

Both extraction capabilities also implement `IPluginSettingsConsumer`, so a saved option applies to the next
indexing pass without a restart. Discovery uses assembly metadata and implemented capability types only —
there is no `plugin.json`.

Default extensions are `.ttf`, `.ttc`, `.otf` and `.otc`, with matching font MIME types. The user can add or
replace extensions in the configuration surface.

**Nothing is extracted to disk and no font file is modified.** The sfnt directory and the `name` table are
read through the host's bounded `IMediaSource`; only the bytes actually parsed are charged to the source
budget.

## Metadata schema

| Key | Type | Meaning |
|---|---|---|
| `Family` | Text | Typographic family (name ID 16), falling back to family (name ID 1) |
| `Subfamily` | Text | Typographic subfamily (name ID 17), falling back to subfamily (name ID 2) |
| `FullName` | Text | Full font name (name ID 4) |
| `PostScriptName` | Text | PostScript name (name ID 6) |
| `Version` | Text | Version string (name ID 5) |
| `Copyright` | Text | Copyright notice (name ID 0) |
| `FaceCount` | Integer | Number of faces in a `.ttc`/`.otc` collection; omitted for a single face |

A field the name table does not carry is **omitted**, never published as an empty string, so "unknown"
stays distinguishable from "none". For a collection, the first readable face is published together with the
face count.

Name records are preferred in this order: Windows Unicode English (`3/1/0x0409`), any Windows or Unicode
record, then Macintosh records. Unicode records are decoded as UTF-16BE; Macintosh records as single-byte
text. Values are trimmed, NUL-padded remnants removed and capped at 512 characters.

## Preview handling

The preview is rendered at index time with SkiaSharp using the font's own glyphs:

- Text comes from the `previewText` setting; line breaks are kept and at most six lines are drawn.
- The text size comes from `previewSize`, clamped to 8–200 pixels; the reference default is 60.
- The canvas is sized to the measured text and clamped to 1600 × 1200 pixels, then checked against the
  aggregate pixel bound before any bitmap is allocated.
- The image is encoded as PNG and stored with the rest of the index-time media results.

The shell shape and bounds are enforced by the host: a preview above the artifact byte limit, an
undecodable glyph set and an unreadable font all produce the explicit "no preview" state. Metadata
extraction is never affected by a preview failure.

### Previews per folder

`previewLimitPerFolder` caps how many previews are generated for fonts that share a folder (zero means no
limit). The host passes an opaque, SHA-256-derived group token for the containing catalog folder in
`MediaItem.GroupKey`; the plugin counts generated previews per token, keeps the counter bounded and resets
it when the relevant settings change. The real folder identifier never reaches the plugin.

## Settings

Persisted by the host's plugin configuration store under the plugin id:

| Key | Values | Default |
|---|---|---|
| `collectionEnabled` | `true`/`false` | `true` |
| `previewsEnabled` | `true`/`false` | `false` |
| `previewText` | Sample text, up to 200 characters | `The quick brown fox jumps over the lazy dog` |
| `previewSize` | 8–200 pixels | `60` |
| `previewLimitPerFolder` | 0–1000, `0` = unlimited | `0` |
| `extensions` | `;`-separated list, with or without the dot | `.ttf;.ttc;.otf;.otc` |

Every value is normalized on read: unreadable booleans and numbers fall back to the documented default,
numbers are clamped into range, blank preview text falls back to the sample, and extensions gain a leading
dot, lose duplicates and invalid entries and are capped at 32. Disabling collection claims no extensions at
all, so neither capability runs. Because the settings participate in the provider identity, changing an
option makes earlier cached results stale and a rescan re-extracts with the new options.

## Failure handling

| Input | Result |
|---|---|
| File shorter than an sfnt header | No values, no preview |
| Unknown sfnt version or `ttcf` collection with no readable face | No values, no preview |
| `numTables` above 512 or a truncated table directory | No values, no preview |
| Name table above 1 MB, truncated or with an unsupported format | No values, no preview |
| Name records pointing outside the table | Offending records skipped, remaining records still used |
| Glyphs that SkiaSharp cannot decode | Metadata still published, no preview |
| Font above 8 MB | Metadata still published up to the host budget, no preview |

## Dependency and license evaluation

| Package | License | Purpose | Policy |
|---|---|---|---|
| `SkiaSharp` 3.119.4 + native assets | MIT | Renders the preview with the font's own glyphs | Allowed |
| `Avalonia` 12.1.6 + `Avalonia.Desktop` | MIT | Configuration surface only | Allowed |

`SkiaSharp` is already a direct dependency of `SkyCD.Plugin.Image`, `SkyCD.Plugin.Rtf`, `SkyCD.Plugin.Pdf`
and `SkyCD.Plugin.EComic`, and Avalonia is the application UI framework. No new transitive dependency is
introduced, and every package complies with `docs/legal/dependency-license-policy.json` (MIT is on the
allow list; no blocked or unknown licenses).
