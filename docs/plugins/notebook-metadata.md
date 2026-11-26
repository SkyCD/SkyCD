# Jupyter notebook metadata plugin

Implements #1245 on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Metadata.Notebook/` → plugin id `SkyCD.Plugin.Notebook`.
Tests: `tests/SkyCD.Plugin.Metadata.Notebook.Tests/`.

This plugin claims `.ipynb` (`application/x-ipynb+json`) and catalogues the notebook's format version,
kernel language, cell counts and leading cell titles while the scanned path is indexed. Discovery uses
assembly metadata and implemented capability types only; there is no `plugin.json`, and a test asserts
none exists.

## What it provides

| Capability | Type | Schema |
|---|---|---|
| Notebook metadata | `NotebookMetadataPlugin : IMediaMetadataCapability` | `skycd.notebook.metadata` v1 |

There is no preview: a notebook's stored HTML/JS/image outputs are never rendered.

## Dependency and current coverage

The plugin reads through `OfficeIMO.Reader.Notebook` 3.6.2 (MIT), the version shared by every OfficeIMO
package in the repository; a test asserts the versions stay in lockstep.

`OfficeIMO.Reader.Notebook` is published as a bounded `.ipynb` reader. The OfficeIMO document model and
importer packages that #1245 also names — `OfficeIMO.Notebook` (`NotebookDocument.Load`,
`NotebookLoadOptions`) and `OfficeIMO.Notebook.Import` (`NotebookImporter`, Quarto `.qmd`, R Markdown
`.Rmd`, `.nb.html`, `.deepnote`, Zeppelin `.zpln`) — are present on the OfficeIMO `master` branch but
**not published to nuget.org** at 3.6.2 or any earlier version, so they cannot be referenced. Until they
ship:

- only `.ipynb` is claimed; the notebook-source formats above stay with no claimant;
- nbformat 3 notebooks, which store cells under `worksheets`, are rejected with
  `legacy-nbformat-unsupported` instead of being normalized to one cell list;
- kernel name/display, execution counts, output counts, attachment counts and import diagnostics are
  not published, because the reader does not expose them.

The schema keeps `CellTitles` and the error fields stable so the later document-model implementation can
add fields without renaming these.

## Metadata schema

| Key | Type | Meaning |
|---|---|---|
| `NotebookFormat` | Text | Always `ipynb` |
| `NbFormatVersion` | Text | `major.minor` from `nbformat`/`nbformat_minor` |
| `Language` | Text | `kernelspec.language`, then `language_info.name` |
| `CellCount` | Integer | Total cells in the source array |
| `IncludedCellCount` | Integer | Cells the bounded reader projected |
| `CellTitles` | Text | First ATX heading of each leading Markdown cell, comma-separated |
| `Valid` | Boolean | Whether the reader accepted the document |
| `ErrorCode`, `ErrorLine`, `ErrorColumn` | Text, Integer, Integer | Parse failure code and one-based position |
| `ByteLength` | Integer | Source length |

A field the notebook does not carry is **omitted**, never published as an empty string: a notebook
without kernel metadata has no `Language`, and a notebook whose leading Markdown cells have no headings
has no `CellTitles`.

`CellCount` and `IncludedCellCount` differ when the reader stops at its cell budget or drops a cell whose
projection is blank. `IncludedCellCount` therefore shows how much of the notebook the counts describe.

## Bounds and safety

The source is read once into memory, and a notebook above `MaxSourceBytes` (16 MiB by default) is
rejected before any byte is charged. The reader then applies its own cell budget (`MaxCells`, 10,000)
and per-cell source bound (`MaxCellCharacters`, 1,000,000). Cell titles are bounded by
`MaxCellTitles` (8) and `MaxTitleCharacters` (200).

The notebook is handled as inert JSON: no kernel runs, code is never executed, widgets are never loaded,
stored HTML/JS is never interpreted or rendered, and no remote resource is fetched. Stored outputs are
not requested from the reader at all (`IncludeOutputs = false`), so output payloads are neither decoded
nor published.

## Failures

Invalid input is a normal metadata result, not a provider failure: `Valid = false` with an error code and
no invented values.

| Code | Meaning | Position |
|---|---|---|
| `empty` | The source has no bytes | — |
| `invalid-json` | Malformed JSON or invalid UTF-8 | line and column |
| `not-a-notebook` | Valid JSON without a `cells` array | — |
| `legacy-nbformat-unsupported` | nbformat 3 `worksheets` layout | — |

A source above `MaxSourceBytes` throws `NotebookSourceTooLargeException`, like every other media provider.
