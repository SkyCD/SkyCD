# XMP sidecar metadata plugin

Implements #728 on the media capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Xmp/` → plugin id `SkyCD.Plugin.Xmp`.
Tests: `tests/SkyCD.Plugin.Xmp.Tests/`.

## What it provides

One capability, discovered from assembly metadata without `plugin.json`:

| Capability | Type | Schema |
|---|---|---|
| Keywords, description and rating from a standalone XMP sidecar | `XmpMetadataPlugin : IMediaMetadataCapability` | `skycd.xmp.metadata` v1 |

The plugin claims the extension `.xmp` and the MIME type `application/rdf+xml`. XMP embedded inside a
JPEG, PNG, WebP, TIFF or RAW container is read by the plugin that claims that container
(`SkyCD.Plugin.Image`), so every carrier is claimed exactly once. A sidecar is the only source claimed
here.

## Fields

| Key | Type | Role | Meaning |
|---|---|---|---|
| `Tags` | Text | Tags | Keywords joined with `; `; **omitted** when absent |
| `Comment` | Text | – | Caption or user comment; **omitted** when absent |
| `Rating` | Integer | – | 1–5 stars; **omitted** when absent or rejected |

Tags carry the `Tags` role, so the host assigns them as catalog tags through the existing media-cache
effects. There is no hierarchical expansion: `lr:hierarchicalSubject` is not read, so
`Places|Beach|Sunset` stays one keyword.

## Property mapping

The plugin reads the properties that photo tools and file managers agree on, from the first packet in the
file:

| Property | Field |
|---|---|
| `dc:subject` (`rdf:Bag`/`rdf:Seq` of `rdf:li`, or a plain value) | `Tags` |
| `pdf:Keywords` (delimited string, split on `;` and `,`) | `Tags` |
| `dc:description` (localized, `x-default` preferred), falling back to `exif:UserComment` | `Comment` |
| `xmp:Rating` (0–5 stars) | `Rating` |
| `MicrosoftPhoto:Rating` (0–99 Shell scale, `-1` rejected) | `Rating` |

Both keyword sources merge and deduplicate case-insensitively. Ratings use the Shell's own star ranges
(1–12 → 1★, 13–37 → 2★, 38–62 → 3★, 63–87 → 4★, 88–99 → 5★); zero, negative ("rejected") and absent
values produce no rating.

XML is parsed with DTD processing prohibited and no external resolver, so a hostile sidecar cannot pull
in external entities. The packet is located either as a wrapped packet (`<?xpacket ...?>` around
`x:xmpmeta`) or as bare `rdf:RDF`, because some tools write the RDF without the wrapper.

## Resource behaviour

- Metadata extraction reads at most the first 1 MiB of the sidecar, in 256 KiB chunks and through the
  host's bounded `IMediaSource`. The packet beyond that prefix is not parsed.
- Cancellation is checked before every read and propagates normally.
- Malformed, truncated or non-XMP input yields no values instead of a failed record, so a sidecar a tool
  left half-written never stops an index pass.
- The source is only ever read; nothing is written back.

## Host integration

- Priority 10, the same as the image plugin's embedded readers; each result is stored separately under
  its own schema id.
- `MediaProviderSelection.StandardMetadata` and `OptInMetadata` select it as a metadata provider; preview
  and hashing passes do not.

## Dependencies and licensing

The reader is pure BCL (`System.Xml.Linq`) and adds no NuGet packages. The general-purpose XMP toolkit
was rejected: its NuGet license is declared as a URL rather than an SPDX expression, which the
dependency-license policy gate reports as unknown and fails. The parsing is shared source from
`Plugins/Shared/MediaMetadata/` (`XmpPacketLocator`, `XmpMetadataReader`, `XmpPacketReader`,
`MetadataScan`, `MetadataText`, `MetadataValues`, `SourcePrefix`), compiled into this assembly and into
`SkyCD.Plugin.Image`, which needs the same code for embedded packets.

## Tests

`tests/SkyCD.Plugin.Xmp.Tests/` builds sidecars in memory. Coverage includes keywords, description and
rating; a rejected rating; `pdf:Keywords` splitting; Microsoft ratings converted to stars; malformed,
truncated and non-XMP payloads returning no values without failing; an empty source; a packet beyond the
1 MiB prefix ignored; cancellation; progress completion; the descriptor claiming only `.xmp` and
`application/rdf+xml`; host selection of the standard metadata pass only; and discovery of the single
capability from assembly metadata, with no manifest and no configuration surface.
