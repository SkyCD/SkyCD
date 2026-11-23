# Issue #486: Add native WinCatalog catalog import and validate legacy collection profiles

## Summary
Implemented a read-only importer for native WinCatalog catalog databases
(`.w3cat`) through the .NET plugin architecture (capability interfaces
`IFileFormatPluginCapability`, `IContentAwareFileFormatCapability`; identity
resolved from assembly metadata; no `plugin.json`).

## New plugin: `Plugins/SkyCD.Plugin.WinCatalog.Native`
- `WinCatalogNativePlugin` — format id `wincatalog-native`, extensions
  `.w3cat`/`.wcat`/`.wca`/`.wcl`, read-only. Payload contract is the shared
  flat node rows (`nodeId`/`parentId`/`kind`/`name`/`sizeBytes`/`properties`),
  so app-side import routing (`MainWindow.ExtractCatalogEntries`) and
  persistence work unchanged.
- `WinCatalogNativeRecognizer` — content-aware detection. Requires the SQLite
  header plus WinCatalog-specific markers (score >= 2, or >= 1 with a `.w3cat`
  name) so arbitrary SQLite files are never claimed. Legacy extensions are
  never claimed here; they are reported at read time.
- `WinCatalogNativeReader` — hardened read-only snapshot (temp copy, read-only
  + `query_only`, no sidecars). Column-tolerant mapping of catalog-like tables;
  tables without a name column are skipped with diagnostics. Bounded item
  count, identifier/blob caps, cancellation checks. Missing catalog tables
  raise unsupported-schema; zero rows raise empty-catalog; SQLite failures
  raise corrupt.
- `WinCatalogNativeHierarchyBuilder` — reconstructs media/folder/file
  hierarchy from full paths, preserving original path, comments, tags, dates,
  and source table as properties.
- Custom exceptions (one per file): `WinCatalogNativeCorruptException`,
  `WinCatalogNativeEmptyCatalogException`,
  `WinCatalogNativeUnsupportedSchemaException`,
  `WinCatalogNativeLegacyProfileException`,
  `WinCatalogNativeUnsupportedVersionException`.
- New dependencies: `Microsoft.Data.Sqlite` 11.0.0 (Apache-2.0) and
  `SQLitePCLRaw.lib.e_sqlite3` 2.1.14 (Apache-2.0); both pass the Trivy
  license gate in `docs/legal/dependency-policy.md`.

## Legacy profiles
`.wcat`/`.wca`/`.wcl` are recognized by extension only to produce the
conversion diagnostic (convert to `.w3cat` in WinCatalog or export CSV/XML).
No direct parsing is claimed; see `docs/plugins/wincatalog.md`.

## Integration
- Registered in `SkyCD.slnx` (plugin + tests).
- Routed through existing File -> Open / CLI `open` flow with content-aware
  detection; progress and cancellation flow through as with other plugins.
  Source files remain unchanged.

## Tests (`tests/SkyCD.Plugin.WinCatalog.Native.Tests`)
Synthetic license-safe SQLite fixtures are generated at runtime with
`Microsoft.Data.Sqlite` (empty, standard multi-item with Unicode, nested,
duplicate names, malformed rows, unrelated SQLite, truncated header, legacy
rejection, cancellation).
- Detection tests: recognition, position restoration, legacy exclusion,
  read-only write, unsupported-schema routing.
- Reader tests: hierarchy, Unicode, nesting, empty/corrupt/cancel/malformed.
- Integration tests: `MainWindow.ExtractCatalogEntries` + Couchbase Lite
  persist/reopen, duplicate-name parent separation, tree verification.

## Docs
- `docs/plugins/wincatalog.md` — user import instructions, supported
  versions, bounds, known loss.
