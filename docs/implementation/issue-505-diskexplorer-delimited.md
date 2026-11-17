# Issue #505: Disk Explorer delimited-text profile

Read/write `disk-explorer-delimited` sub-profile in
`Plugins/SkyCD.Plugin.DiskExplorer/Delimited/` on the shared
`fix/507-diskexplorer-xml` feature branch (PR targets that branch, per the
sub-issue rule). No new dependencies, no new projects.

## Pipeline

- `DiskExplorerDelimitedParser`: BOM/UTF-8/1252 detection (seekable
  streams), single-reader streaming with quote-at-field-start semantics,
  4-separator auto-detection with extension tiebreak, header-required
  mapping onto the shared `DiskExplorerParseResult` shape, path/category
  hierarchy resolution with topological parent-first ordering, ancestor and
  volume synthesis, partial flag. Unknown labels become
  `DiskExplorer.<LABEL>` extras. Custom exceptions
  (`DiskExplorerDelimitedMalformedException`,
  `DiskExplorerDelimitedAmbiguousException`,
  `DiskExplorerDelimitedEmptyReportException`), one class per file, fixed
  message templates.
- `DiskExplorerDelimitedExporter`: tree rebuild from row ids, relative/full
  path computation, configurable columns/separator/encoding/newlines,
  formula guard (`'=...` prefix) with raw-compat escape hatch.
- `DiskExplorerDelimitedRecognizer` / `DiskExplorerDelimitedPlugin`:
  header-vocabulary recognition with VVV veto (PhysicalPath, Last modified,
  Ext) and XML rejection; format id `disk-explorer-delimited`, extensions
  `.tsv`/`.csv`/`.txt`, read + write. Shared
  `DiskExplorerHierarchyBuilder` / `DiskExplorerReportDocument` reused;
  partial imports annotated on the summary.

## Tests

`DiskExplorerDelimitedTests` in `tests/SkyCD.Plugin.DiskExplorer.Tests`
(xunit, golden fixtures under `Fixtures/DiskExplorer/delimited-*`): tab /
comma / semicolon / pipe dialects, quoting and embedded commas/quotes/
newlines, lowercase labels, unknown-column retention, flat partial imports,
Windows-1252 and UTF-8 BOM decoding, headerless/inconsistent/empty
failures, recognizer true/false cases, export round-trip, formula guard
with 1252 + CRLF, cancellation. Fixture globs added to the test project.
