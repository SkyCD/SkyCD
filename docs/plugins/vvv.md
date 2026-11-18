# VVV Export Import Plugin

## Overview
The `vvv` plugin provides import-only support for file-list exports saved by
**VVV (Virtual Volumes View)** — comma-, tab-, or pipe-separated values with
optional headers. VVV always quotes string fields (`"` escaped as `""`),
leaves sizes and dates unquoted, and writes one row per file. Physical
exports (all volumes, one volume, or one folder) carry seven columns
(`Volume, Path, Name, Size, Ext, Last modified, Description`); virtual-folder
exports carry six (`Name, Size, Ext, Last modified, PhysicalPath,
Description`). Column shapes verified against the VVV export source
(`exportdata.cpp`: `OnOkClick`, `ExportOnePhysicalFolder`,
`ExportOneVirtualFolder`, `FormatStringForCSV`, `WriteHeaders`).

## User Export Instructions in VVV
1. In VVV choose **Export data** and pick a separator (Comma, TAB, or Pipe).
2. Keep **Include headers** checked when possible (headerless exports import
   too, but only by explicit format selection — see below).
3. Choose the scope: **All physical volumes**, **Selected physical
   volume/folder**, or **Selected virtual folder**.
4. Save the file (`.csv`/`.txt` suggested) in UTF-8.

## User Import Instructions in SkyCD
1. In SkyCD, choose **File -> Open...**.
2. Select your exported file. Content-aware detection recognizes VVV exports
   that include the English header row. Headerless or localized-header
   exports are not auto-claimed (to avoid confusing them with generic CSV
   files); select the **VVV Export** format explicitly to import them.
3. Volumes become media nodes with reconstructed folder hierarchies and file
   entries. Source files are never modified.

## Supported Versions & Variants
- **Software**: VVV 1.x export dialog (separator choice Comma/TAB/Pipe,
  optional headers, three export scopes).
- **Encodings**: UTF-8 only (with or without BOM). Other encodings are
  rejected with a diagnostic.
- **Headers**: English headers recognized for auto-detection; any single
  unrecognized header row whose size column is non-numeric while data rows
  are numeric is treated as a localized header (reported as a warning);
  headerless exports are detected structurally when explicitly selected.
- **Dates**: `Last modified` keeps the raw VVV locale-formatted text
  (`FormatDate + FormatTime`); no normalization is attempted because the
  format follows the exporting machine's locale.
- **Empty folders and folder rows**: VVV exports files only (folders are
  recursed, never listed), so empty folders cannot be represented; folder
  nodes are synthesized from file paths.

## Mapping and Ambiguity Rules
- Identity: physical files key on volume + path + name, so repeated
  filenames on separate volumes stay distinct and re-imports map stably.
  Rows with an empty volume (physical) or empty name are skipped with a
  counted reason instead of inventing media names or parents.
- Sizes must parse as non-negative integers; bad sizes skip the row with a
  reason. Paths have `\` normalized to `/` with volume prefixes stripped.
- Virtual exports group files under one media node named after the export
  file; per-file `PhysicalPath` preserves the origin and rebuilds the
  physical folder chain. The virtual folder structure itself is lost and
  the summary says so.
- Delimiter resolution requires every non-empty row to share one 6- or
  7-column shape under a single separator; mixed shapes, single-column
  input, and header-only files fail with actionable errors naming the
  expected layouts. Ties prefer the header-vocabulary match.

## Backup files (.vvvbk)

SkyCD does not directly import `.vvvbk` backups or native `.vvv` catalogs.
Keep the backup unchanged and restore it with a compatible VVV installation to
a new catalog, then export all physical volumes with headers and a TAB separator.
Ensure the export is UTF-8, converting it if necessary, before importing it. This
does not preserve virtual hierarchy or audio metadata. See the
[backup compatibility assessment](../implementation/issue-511-vvvbk-assessment.md).

## Known Loss of Information
- **Virtual folder structure**: not exported by VVV, only physical origin.
- **Folder metadata/dates**: folders are synthesized, carrying no dates.
- **Empty folders**: never present in exports.
