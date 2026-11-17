# Issue #490: Add bounded CD Catalog Expert 9.x ZDC container codec

## Summary

Implemented a native stream codec plus read/write plugin for CD Catalog
Expert 9.x catalogs (`.zdc`, `chunkSize = 4096`) through the .NET plugin
architecture (capability interfaces `IFileFormatPluginCapability`,
`IContentAwareFileFormatCapability`; identity from assembly metadata; no
`plugin.json`).

## New plugin: `Plugins/SkyCD.Plugin.CdCatalogExpert`

- `ZdcPlugin` — format id `cdcatalogexpert-zdc`, extension `.zdc`, read +
  write. Recognition requires both container magics, the ZDC chunk size, and a
  chunk-aligned length; other chunk sizes are rejected as corruption.
- `ZdcReader` — bounded iterative parser implementing the specified chunk
  grid: two-magic header validation, directory-tree walk with XOR checksums
  and zero-fill checks, 512-byte member records with strict name rules,
  per-member file-index chains, file-data collection with overlap/duplicate
  rejection, AACS unwrapping (size agreement, exact zlib completion, CRC),
  well-formed-XML and JPEG SOI/EOI payload checks, and encrypted
  key/flag detection.
- `ZdcAacsCodec` — AACS envelope wrap/unwrap over BCL `ZLibStream`;
  compression field `5`, CRC-16/IBM. Corrupt deflate data is wrapped as
  corruption, never leaked as compression-library errors.
- `ZdcWriter` — deterministic layout (chunks `0..3` reserved, tree from chunk
  4, per-member index chains, data chunks last; canonical order index/discs/
  covers/others; missing index synthesized empty) recomputing chains, counts,
  checksums, and envelopes while preserving raw reserved fields.
- `ZdcReportBuilder` — converts between containers and the shared flat node
  rows; preserved raw fields travel as plain strings, payloads as Base64 with
  SHA-256, non-zero type flags listed in the import summary.
- Custom exceptions (one per file): `ZdcCorruptException`,
  `ZdcEncryptionNotSupportedException`.
- No new NuGet dependencies (BCL only: `ZLibStream`, `XmlReader`,
  SHA-256); license policy unaffected.

## Known open points (documented, cheap to revise after genuine-file review)

- Domain mapping is structural only (media root, flat file members); disc XML
  is preserved opaquely because the profile stores no inter-member relations.
- The writer profile (reserved chunks, canonical member order, synthesized
  empty index) mirrors the specified layout; genuine-file verification of
  reserved/profile values is still open.
- Empty members (zero stored length, no chain) are accepted and round-trip;
  non-empty XML/JPEG payloads must validate.

## Integration

- Registered in `SkyCD.slnx` (plugin + tests).
- Routed through existing File -> Open / CLI open/save flows with
  content-aware detection; progress and cancellation flow through as with
  other plugins. Source files remain unchanged.

## Tests (`tests/SkyCD.Plugin.CdCatalogExpert.Tests`)

Runtime-generated synthetic containers (`ZdcFixture`) serialized by the plugin
writer, plus byte-level mutations for malformed inputs. Checked-in
`Fixtures/sample.zdc` (own writer output) with `sample.manifest.json`
(member manifest + decompressed payload hashes) is the compatibility oracle:
`ZdcCheckedInFixtureTests` compares manifest/hashes and asserts byte-identical
rewrite.

- Golden tests: header offsets, member-record offsets, AACS envelope fields,
  XOR/CRC-16 check vectors, cross-chunk (100 KiB) and multi-index-chain
  (~4.3 MiB) payloads, two directory chunks, empty members, deterministic
  and byte-identical output.
- Reader tests: media/member rows, raw-field preservation, payload hashes,
  content sniffing and position restore, non-seekable streams.
- Writer tests: canonical member order, index synthesis, name rules, fresh
  rows, plugin read/write round trip, invalid-payload failure.
- Negative tests: truncation at several points, bad magics/chunk size,
  misaligned length, declared/lying counts, premature termination, tree
  cycles and revisits, non-zero unused bytes, bad checksums, malformed names
  (no NUL, non-ASCII, separators, `..`, absolute, duplicates), non-zero
  reserved fields, stored-length overflow, missing/chained data, bad AACS
  fields, compressed/wrapper size mismatches, corrupt deflate, CRC mismatch,
  bad padding, excessive inflation, invalid XML/JPEG, encryption flag/keys,
  unsupported flags, cancellation, plugin failure result.
- All 74 tests pass (executed via a reflection runner; `dotnet test`
  testhost is denied in this sandbox — environmental, proven by the same
  failure on the unmodified `SkyCD.Plugin.Csv.Tests` project).

## Docs

- `Plugins/SkyCD.Plugin.CdCatalogExpert/BINARY-LAYOUT.md` — full layout next
  to the codec.
- `docs/plugins/cdcatalogexpert-zdc.md` — user import/export guide.
