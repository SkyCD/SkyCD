# Issue #457: Tellico collection import

Read-only `tellico` file-format plugin: native `.tc` archives and plain
Tellico XML, mapped onto catalog rows with typed custom fields. No new
dependencies (ZIP via inbox `System.IO.Compression`).

## Pipeline (`Plugins/SkyCD.Plugin.Tellico`)

- `TellicoParser`: container sniffing (ZIP magic vs XML), archive guards
  (entry count, per-entry/total sizes, traversal and absolute-path
  rejection, `tellico.xml` with legacy `bookcase.xml` fallback), secure
  `XmlReader` settings (external DTD never fetched, entity expansion
  bounded), frame-stack field collection (structured dates, repeated
  elements, tables), field definitions from the document (`type` 1/2/3/4/
  6/7/8/10/12/14, `AllowMultiple` flag, unknown types as text with notes),
  image-section resolution (metadata only), borrower/loan cross-reference
  applied after the full collection is read, filters/macros/bibtex noted
  as unsupported, unknown data retained as `Tellico.*` extension metadata.
  Custom exceptions (`TellicoMalformedException`,
  `TellicoUnsupportedSchemaException`, `TellicoEmptyCollectionException`,
  `TellicoUnsafeArchiveException`), one class per file, fixed message
  templates.
- `TellicoHierarchyBuilder`: one media node per collection plus one `entry`
  row per item (`tellico:{collection}:{id}` node ids, typed property
  values, display-name fallback chain).
- `TellicoRecognizer` / `TellicoPlugin`: ZIP magic plus `tellico.xml`
  member name, or `<tellico` root with `syntaxVersion`; `.tc`/`.xml`,
  read-only.
- Loader mapping (`MainWindow.BuildEntriesFromRows`): explicit `entry` to
  `Media` case with a comment, so collection items are never files. Covered
  by `CatalogEntryKindTests` in `SkyCD.App.Tests`.

## Tests

`tests/SkyCD.Plugin.Tellico.Tests` (xUnit, DTD-shaped synthetic fixtures
built in code, archives generated with `ZipArchive`): typed fields,
multi-value lists and nested tables, image metadata, loans incl. dangling
refs, provisional ids, unknown retention, old/new syntax versions,
empty/wrong-root/DTD-entity/expansion/depth failures, zip-slip, archive
without document, cancellation; recognizer true/false cases; plugin
end-to-end archive and XML reads plus write refusal.
