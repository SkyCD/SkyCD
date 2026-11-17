# Issue #454: Implement NeoFinder configurable text-export import plugin

## Summary

Implemented a read-only importer for NeoFinder configurable text exports
("Text with Tab") through the .NET plugin architecture (capability interfaces
`IFileFormatPluginCapability`, `IContentAwareFileFormatCapability`; identity
resolved from assembly metadata; no `plugin.json`).

## New plugin: `Plugins/SkyCD.Plugin.NeoFinder.Text`

- `NeoFinderTextPlugin` — format id `neofinder-text`, extensions
  `.txt`/`.tsv`, read-only. Payload contract is the shared flat node rows
  (`nodeId`/`parentId`/`kind`/`name`/`sizeBytes`/`properties`), so app-side
  import routing (`MainWindow.ExtractCatalogEntries`) and persistence work
  unchanged. Competing `.txt` claims (e.g. PlainText) lose routing only when
  the NeoFinder header layout is positively recognised.
- `NeoFinderTextProfile` / `NeoFinderTextColumn` — header-driven column
  mapping with English core headers plus best-effort localized variants.
  Duplicate semantic mappings, missing Name columns and headerless files are
  rejected as ambiguous with an actionable message.
- `NeoFinderTextEncodingDetector` / `NeoFinderTextEncodingProfile` — strict
  BOM-first decoding policy (UTF-8/16, Windows-1252 fallback, no lossy
  decoding), shared between recognition and import.
- `NeoFinderTextHierarchyBuilder` — reconstructs media/folder/file hierarchy
  from macOS paths (POSIX and classic `:` separators), groups multi-catalog
  exports per volume/catalog, normalizes Unicode/dates/sizes, and warns
  (instead of failing) when only flat placement is possible. Bounded skipped
  rows, cancellation checks, identifier dedupe.
- Custom exceptions (one per file): `NeoFinderTextCorruptHeaderException`,
  `NeoFinderTextEmptyExportException`,
  `NeoFinderTextUnsupportedEncodingException`,
  `NeoFinderTextAmbiguousLayoutException`.
- No new NuGet dependencies (reuses `SkyCD.Plugin.Csv` `CsvParser`); license
  policy unaffected.

## Integration

- Registered in `SkyCD.slnx` (plugin + tests).
- Routed through existing File -> Open / CLI `open` flow with content-aware
  detection; progress and cancellation flow through as with other plugins.
  Source files remain unchanged. The import is side-effect free until the host
  persists it, so cancellation cannot corrupt an existing catalog.

## Tests (`tests/SkyCD.Plugin.NeoFinder.Text.Tests`)

Runtime-generated synthetic fixtures mirroring the documented export layout
(see `Fixtures/README.md`): standard/comma/semicolon/German layouts,
macOS `:` paths, multi-catalog grouping, duplicate names, Unicode,
malformed rows, headerless/ambiguous/empty/unrelated/truncated inputs,
cancellation.

- Detection tests: recognition, SkyCD-generic and WinCatalog-CSV exclusion,
  position restoration, read-only write.
- Reader tests: hierarchy, Unicode, multi-catalog media roots, flat warning,
  ambiguous/empty/corrupt/cancel/malformed, size/date unit coverage.
- Integration tests: `MainWindow.ExtractCatalogEntries` + Couchbase Lite
  persist/reopen, duplicate-name parent separation.

## Docs

- `docs/plugins/neofinder-text.md` — recommended export configuration,
  supported versions/encodings, column mapping, bounds, known loss.
