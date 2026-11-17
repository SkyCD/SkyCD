# Issue #452: VVV CSV/TSV import

Read-only `vvv` file-format plugin for Virtual Volumes View file-list
exports. Reuses the shared `SkyCD.Plugin.Csv` parser (explicit delimiters,
multi-line quoted fields) with VVV-specific structural resolution and
schema mapping; no new dependencies.

## Pipeline (`Plugins/SkyCD.Plugin.Vvv`)

- `VvvParser`: strict UTF-8 read; delimiter trials over comma/tab/pipe
  requiring one uniform 6- or 7-column shape (ties prefer the header
  vocabulary); header detection by English vocabulary or by the
  non-numeric-size heuristic (localized headers warned, headerless supported
  structurally); row mapping with skip reasons for empty volume/name and
  non-numeric size; `VvvEmptyReportException` when no entries remain.
  Custom exceptions (`VvvMalformedException`, `VvvAmbiguousFormatException`,
  `VvvEmptyReportException`), one class per file, fixed message templates.
- `VvvHierarchyBuilder`: one media node per physical volume (one file-stem
  node for virtual exports), synthesized folder chains, file rows with
  `Extension`/`LastModified`/`Description`/`PhysicalPath`/`VvvVolume`
  properties; sanitized unique node ids; stable across re-imports.
- `VvvRecognizer` / `VvvPlugin`: auto-detection requires the English header
  row (generic `skycd-csv` files never collide: they use a different header);
  headerless VVV imports work through explicit format selection because the
  parser, not the recognizer, validates the layout. Extensions
  `.csv`/`.txt`/`.tsv`, read-only.

## Tests

`tests/SkyCD.Plugin.Vvv.Tests` (xUnit, synthetic fixtures under
`Fixtures/Vvv`): comma/tab/pipe scopes, quoting/unicode, headerless and
localized headers, repeated filenames, virtual physical-path grouping, skip
reasons, ambiguous/single-column/empty/non-UTF-8 failures, cancellation,
hierarchy shape with stable ids, detection true/false cases, explicit
headerless read, write refusal.
