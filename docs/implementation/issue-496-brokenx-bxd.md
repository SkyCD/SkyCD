# Issue #496: Add Broken X Database (.bxd) read/write and import/export plugin

## Summary

Implemented a native codec plus read/write plugin for Broken X Disk Manager
databases (archive version `0x40`) through the .NET plugin architecture
(capability interfaces `IFileFormatPluginCapability`,
`IContentAwareFileFormatCapability`; identity from assembly metadata; no
`plugin.json`).

## New plugin: `Plugins/SkyCD.Plugin.BrokenX`

- `BxdPlugin` — format id `brokenx-bxd`, extension `.bxd`, read + write.
  Recognition requires the exact 21-byte signature and version byte; legacy and
  future versions are rejected with `BxdUnsupportedVersionException`.
- `BxdReader` — bounded iterative parser implementing the specified preorder
  stack algorithm: three string-length forms, three property payload forms,
  fixed item fields, exact item counts, no trailing data, depth cap, no
  recursion. Password-protected records abort with
  `BxdEncryptedRecordException` naming the record.
- `BxdWriter` — preorder serializer recomputing structural bits from the
  exported tree while preserving base flags, fixed fields, raw name bytes and
  every property (unknown descriptors and order included).
- `BxdReportBuilder` — converts between the database tree and the shared flat
  node rows; preserved raw fields travel as plain strings so rows survive host
  serialization. Fresh rows without preserved fields write with neutral
  defaults and canonical empty root properties.
- `BxdMergeService` — deterministic multi-source merge (first source wins,
  conflicts warned, dangling parents repaired).
- `BxdSubtreeSelector` — selected roots plus descendants; unknown or empty
  selections rejected with `BxdSelectionException`.
- Custom exceptions (one per file): `BxdCorruptException`,
  `BxdUnsupportedVersionException`, `BxdEncryptedRecordException`,
  `BxdSelectionException`.
- No new NuGet dependencies; license policy unaffected.

## Known open points (documented, cheap to revise after genuine-file review)

- Domain mapping is structural only (media root, folders from parentage, leaf
  files); no kind-bit or property-descriptor semantics are claimed because the
  specification does not assign them.
- The protected-record marker (base flag bit `0x0001`) is a codec convention;
  detection fails loudly rather than risking silent data loss.

## Integration

- Registered in `SkyCD.slnx` (plugin + tests).
- Routed through existing File -> Open / CLI open/save flows with content-aware
  detection; progress and cancellation flow through as with other plugins.
  Source files remain unchanged.

## Tests (`tests/SkyCD.Plugin.BrokenX.Tests`)

Runtime-generated synthetic fixtures (see `Fixtures/README.md`): canonical
empty bytes asserted against the specification hexdump, one item, siblings,
three-level nesting, Unicode, unknown properties, 1 MiB blobs, 70000-byte
names, encrypted records, malformed/truncated/unsupported-version inputs,
70-level nesting, lying counts, trailing garbage, unclosed folders.

- Detection tests: signature/version gating, position restore, write routing.
- Reader tests: tree shape, field preservation, Unicode, unknown properties,
  every specified error, cancellation.
- Writer tests: canonical empty bytes, length-form selection, read -> write ->
  read round trips across all fixtures, generic-row fresh writes.
- `BxdStructuralValidator` — independent spec-only reimplementation used to
  cross-validate writer output and every malformed fixture.
- Merge/subtree tests: deterministic union, first-wins conflicts, descendant
  selection, selection errors.
- Integration tests: `MainWindow.ExtractCatalogEntries` + Couchbase Lite
  persist/reopen, duplicate-name parent separation.

## Docs

- `Plugins/SkyCD.Plugin.BrokenX/BINARY-LAYOUT.md` — full layout next to the codec.
- `docs/plugins/brokenx-bxd.md` — user import/export guide, round-trip
  behavior, merge/export operations, protected records, limits.
