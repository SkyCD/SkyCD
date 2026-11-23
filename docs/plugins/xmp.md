# XMP sidecar metadata plugin

Implements #728 on the media capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Metadata.Xmp/` → plugin id `SkyCD.Plugin.Metadata.Xmp`.
Tests: `tests/SkyCD.Plugin.Metadata.Xmp.Tests/`.

## What it provides

One capability, discovered from assembly metadata without `plugin.json`:

| Capability | Type | Schema |
|---|---|---|
| Keywords, description and rating from a standalone XMP sidecar | `XmpMetadataPlugin : IMediaMetadataCapability` | `skycd.xmp.metadata` v1 |

The plugin claims the extension `.xmp` and the MIME type `application/rdf+xml`. XMP embedded inside a
JPEG, PNG, WebP, TIFF or RAW container is read by the plugin that claims that container
(`SkyCD.Plugin.Metadata.Image`), so every carrier is claimed exactly once. A sidecar is the only source claimed
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

The packet is located either as a wrapped packet (`<?xpacket ...?>` around `x:xmpmeta`) or as bare
`rdf:RDF`, because some tools write the RDF without the wrapper; a byte-slice locator cuts it out of the
bounded prefix. Parsing and property access are delegated to XmpCore, the XMP toolkit that
`MetadataExtractor` also bundles on every target framework, so the plugin carries no hand-written XML
reader.

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

| Package | Version | License | Why |
|---|---|---|---|
| `XmpCore` | 6.1.10.1 | BSD-3-Clause | Packet parsing and property access |

The package declares only a legacy license URL rather than an SPDX expression, so the Trivy scan in
[dependency-policy.md](../legal/dependency-policy.md) reports it as UNKNOWN and does not fail; Adobe's
XMP SDK `LICENSE` and the upstream project README are the BSD-3-Clause evidence. The same package arrives
transitively through
`MetadataExtractor` in the image plugin, so both plugins use one XMP implementation. The locator, mapping
logic and scan helpers are shared source from `Plugins/Shared/MediaMetadata/` (`XmpPacketLocator`,
`XmpMetadataReader`, `XmpPacketReader`, `MetadataScan`, `MetadataText`, `MetadataValues`, `SourcePrefix`),
compiled into this assembly and into `SkyCD.Plugin.Metadata.Image`, which needs the same code for embedded
packets.

## Tests

`tests/SkyCD.Plugin.Metadata.Xmp.Tests/` builds sidecars in memory. Coverage includes keywords, description and
rating; a rejected rating; `pdf:Keywords` splitting; Microsoft ratings converted to stars; malformed,
truncated and non-XMP payloads returning no values without failing; an empty source; a packet beyond the
1 MiB prefix ignored; cancellation; progress completion; the descriptor claiming only `.xmp` and
`application/rdf+xml`; host selection of the standard metadata pass only; and discovery of the single
capability from assembly metadata, with no manifest and no configuration surface.
