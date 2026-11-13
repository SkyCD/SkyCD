# Issue #447: Add WinCatalog XML report import plugin

## Summary
Implemented a read-only importer for XML reports exported by WinCatalog, following the
architecture of the existing WinCatalog CSV importer (#448). Implemented through the .NET
plugin architecture: capability interfaces (`IFileFormatPluginCapability`,
`IContentAwareFileFormatCapability`) implemented directly; identity resolved from assembly
metadata; no `plugin.json`.

## New plugin: `Plugins/SkyCD.Plugin.WinCatalog.Xml`
- `WinCatalogXmlPlugin` — format id `wincatalog-xml`, extensions `.xml`, read-only. Payload
  contract identical to the WinCatalog CSV importer (flat node rows with
  `nodeId`/`parentId`/`kind`/`name`/`sizeBytes`/`properties`), so the app-side import routing
  (`MainWindow.ExtractCatalogEntries`) and persistence work unchanged.
- `WinCatalogXmlRecognizer` — content-aware detection. Scores the document vocabulary
  (media/folder/file element names, WinCatalog field names, `{@slash}` escape tokens) and
  explicitly rejects SkyCD's native XML catalog namespace (`urn:skycd:catalog`) and generic
  XML. This keeps the format recognition specific enough to avoid conflicts with the
  `skycd-xml` plugin sharing the `.xml` extension.
- `WinCatalogXmlParser` — hardened streaming parse (`DtdProcessing.Prohibit`, `XmlResolver =
  null`, bounded document size / depth 256 / 1,000,000 items). Maps element/attribute names
  through `WinCatalogXmlProfile` (which reuses the localized CSV column aliases); unmapped
  names are recorded as unsupported-field diagnostics; malformed records are skipped with
  reasons.
- `WinCatalogXmlHierarchyBuilder` — two composition modes:
  - *Nested*: media/folder/file elements reconstruct the hierarchy directly; entries without
    an exported disk ancestor get a synthesized media root (marks the report partial).
  - *Flat*: items with `Catalog Path`/`File Path` fields are delegated to the shared
    `WinCatalogHierarchyBuilder` from the CSV plugin (folder synthesis and partial-report
    detection behave exactly like CSV reports).
  - Sizes and dates reuse `ParseSize`/`NormalizeDate`; paths are preserved as
    `WinCatalogPath`/`WinCatalogFilePath` properties; detected schema version is stored as
    `WinCatalogSchemaVersion` and in the summary (provenance).
- Custom exceptions (one per file): `WinCatalogXmlRootException`,
  `WinCatalogXmlEmptyReportException`, `WinCatalogXmlMalformedDocumentException`,
  `WinCatalogXmlUnsupportedSchemaException` (unsupported declared versions are rejected with
  a useful diagnostic).
- Reuses the shared `SkyCD.Plugin.WinCatalog.Csv` parsing/mapping helpers; no new NuGet
  dependencies (license policy unaffected).

## Integration
- Registered in `SkyCD.slnx` (plugin + tests).
- Import is routed through the existing File -> Open / CLI `open` flow with content-aware
  detection; progress (`IProgress<int>`) and cancellation flow through
  `CatalogLoadService` as with other format plugins. Source files remain unchanged.

## Tests (`tests/SkyCD.Plugin.WinCatalog.Xml.Tests`)
Fixtures under `Fixtures/WinCatalogXml` (redistributable, synthetic, sanitized; version/export
settings recorded in `README.md`): nested English (multi-media, metadata), nested Russian
(UTF-8 BOM, Cyrillic names, European sizes/dates), flat search results (partial), empty
fields, malformed XML, DTD/entity attempt.
- Detection tests: content recognition, position restoration, manager disambiguation
  against `skycd-xml`.
- Hierarchy tests: nested/flat mapping, metadata preservation, locale normalization,
  empty fields, malformed/DTD rejection, unsupported schema version, read-only write.
- Security tests: DTD prohibition, deep-nesting bound, cancellation, hardened settings.
- Integration tests: `CatalogLoadService` + `FileFormatManager` + Couchbase Lite
  persist/reopen for nested multi-media, Unicode, and flat partial reports.

## Docs
- `docs/plugins/wincatalog-xml.md` — user export/import instructions, supported versions,
  known loss of information.
- `docs/migration/related-format-support-tracker.md` — added #447 entry.
