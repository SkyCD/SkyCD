# DiskCatalogMaker text-export import

The `diskcatalogmaker-text` plugin imports DiskCatalogMaker text exports (`.txt`, `.tsv`).
This plugin covers the documented Export Text route only. Native v5 catalogs (`.dcmf`,
`.dcmd` packages, thumbnails) are handled by the separate
[`diskcatalogmaker-v5` plugin](diskcatalogmaker-v5.md); legacy `.dcm` and
`.toastcat` catalogs remain out of scope.

No genuine DiskCatalogMaker export with redistribution rights could be located, so no
tested app version is claimed. The fixtures are synthetic data modeled on the
[official export instructions](https://diskcatalogmaker.com/faq/exporttext.html), the
CDFinder/NeoFinder import guide (which consumes DiskCatalogMaker output as Tab
Delimited Text), and a published field report of an export header
(`Name`, `Size`, `Type`, `Creator`, `Date Modified`). See the
[fixture provenance](../../tests/SkyCD.Plugin.FileFormat.DiskCatalogMaker.Tests/Fixtures/DiskCatalogMaker/README.md).

## Exporting from DiskCatalogMaker

1. In the catalog window list view, check the **Path** attribute in the View menu
   ("Show Attributes in List View"). Without Path, SkyCD cannot rebuild the folder
   hierarchy and the import fails with a descriptive missing-path error.
2. Attribute order (apart from Name, which DiskCatalogMaker fixes first) is yours:
   headers are mapped by label, in any order and in several UI languages.
3. Export via **File -> Export Text…**, preferably as Tab Delimited Text (`.txt`).
   In SkyCD, open the resulting file with **File -> Open**.

## Recognition and encoding

A file is recognised by content, not by extension: the first header field must be
the Name attribute, a Path column must be present, and at least one further
known attribute (`Size`, `Kind`/`Type`, dates, `Creator`, `Type Code`,
`Comments`, `Label`, `Disk`/`Volume`) must follow. Headers distinctive to other
catalog tools (WinCatalog catalog paths and checksums, Disk Explorer structural
labels) veto the match, so neighbouring `.txt`/`.tsv` formats are not stolen.

Recognition and import use the same strict decoding policy:

1. A BOM selects UTF-8 or UTF-16 LE/BE. Malformed data under that declared encoding is rejected.
2. Without a BOM, an alternating NUL pattern in the leading ASCII header identifies UTF-16 LE/BE.
3. Otherwise valid UTF-8 takes precedence.
4. If UTF-8 is invalid, Windows-1252 is accepted only when none of its undefined bytes occur.
5. No lossy replacement-character decoding is used. `DetectedEncoding` in the import summary
   records the selected profile.

Tab, comma and semicolon delimiters are detected automatically; tab is the documented
and fixture-tested export shape.

## Hierarchy and partial exports

Media roots come from the leading `/Volumes/<Disk>/` mount prefix, the first path
segment, or an explicit `Disk`/`Volume` column when present. POSIX slash paths are
primary; classic HFS colon paths (`Disk:Folder:File`) are supported when no slash
is present. (Backslashes are preserved: they are legal macOS filename characters,
not separators.)

Folder rows use a folder-like `Kind` (`Folder`, `Ordner`, …) or a trailing `/`.
Missing intermediate folders are synthesized, and such reports — like reports with
skipped malformed rows — are marked partial in the import summary. Rows without a
usable Path are skipped with a per-row reason; unknown columns are listed in
`UnsupportedFields`. Absent attributes are reported by omission: only exported
metadata (sizes, dates, creator/type codes, comments, labels, volume identity) is
preserved. The plugin is read-only and does not write DiskCatalogMaker exports.
