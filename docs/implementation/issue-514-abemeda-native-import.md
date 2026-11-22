# Issue #514: Native abeMeda/NeoFinder catalog importer

## Summary

Added a read-only native catalog capability in
`Plugins/SkyCD.Plugin.FileFormat.AbeMeda/Native` for the documented classic container
(`.cdw`) and recognition of the extended generation (`.abeMeda7`,
`.neofinder7`). The importer validates the observed `DWDRDNBA` header, parses the
36-byte record table, preserves unknown header fields, and publishes embedded
artifact records in an explicitly unassociated folder. Item hierarchy, names,
annotations, and extracted media metadata are not decoded because their layout has
not been verified against genuine fixtures.

## Files

- `AbeMedaNativePlugin` — format id `abemeda-native`, extensions `.cdw`,
  `.abeMeda7`, `.neofinder7`, read-only, content-aware detection.
- `AbeMedaNativeRecognizer` — signature and sentinel check with position
  preservation.
- `AbeMedaNativeHeaderReader` / `AbeMedaNativeHeader` — verified fixed header
  parsing and checked bounds.
- `AbeMedaNativeBinaryReader` — bounded little-endian reads over a seekable
  stream; no allocation from untrusted lengths.
- `AbeMedaNativeEntryReader` / `AbeMedaNativeEntry` — 36-byte record parsing,
  strict ordinal ordering, handler/range diagnostics, warning cap.
- `AbeMedaNativeCatalogReader` / `AbeMedaNativeCatalog` — generation dispatch and
  read orchestration.
- `AbeMedaNativeGenerationDetector` — classic versus extended dispatch by suffix.
- `AbeMedaNativeHierarchyBuilder` / `AbeMedaNativeReportDocument` /
  `AbeMedaNativeImportSummary` — flat row contract for the existing catalog
  loader, plus version/section/artifact diagnostics.
- Exceptions (one class per file): `AbeMedaNativeUnsupportedSchemaException`,
  `AbeMedaNativeUnsupportedVersionException`,
  `AbeMedaNativeUnsupportedLayoutException`,
  `AbeMedaNativeUnsupportedGenerationException`,
  `AbeMedaNativeCorruptCatalogException`.

## Imported data

- Media node named after the catalog file stem.
- Version and compatibility markers, integrity field, header size, record stride,
  section offset/length, block size, declared/imported/skipped record counts.
- Unknown header fields at `0x2C`, `0x30`, `0x34`, and `0x38` as raw hex.
- Every well-formed record as an artifact row under
  **Embedded artifacts (unassociated)**: ordinal, BLOB value, flags, handler,
  MIME hint for JPEG/PNG/GIF, record offset, and both ranges with an
  in-file flag.
- Salvage warnings for non-increasing ordinals, non-text handlers, and values
  beyond the file length.

## Deliberate limits

- Item hierarchy, names, paths, comments, labels, ratings, and media metadata are
  not decoded. The format notes only establish the fixed header and the 36-byte
  record table; guessing the item tables would produce incorrect catalogs.
- The BLOB value and ranges are preserved but not interpreted or extracted.
- The integrity field is preserved verbatim and is not verified as a checksum.
- Extended catalogs are recognized and rejected with an actionable diagnostic.
- Genuine fixtures and item-table verification remain tracked by `#589`, `#590`,
  and `#591`.

## Tests

`tests/SkyCD.Plugin.FileFormat.AbeMeda.Tests` (native suite, 29 tests) uses runtime-generated
synthetic containers built from the documented offsets. No vendor bytes are
committed. Coverage includes recognition, classic import, empty catalogs, unknown
header preservation, malformed/unsupported diagnostics, extended gating,
cancellation, progress, read-only behavior, and persistence/reopen through
`MainWindow.ExtractCatalogEntries` and Couchbase Lite.

## Validation

- `dotnet build SkyCD.slnx --configuration Release`
- `dotnet test tests/SkyCD.Plugin.FileFormat.AbeMeda.Tests/...`
- `dotnet format SkyCD.slnx --verify-no-changes --verbosity minimal`
