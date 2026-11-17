# Issue #458: GCstar collection import

Read-only `gcstar` file-format plugin: streaming parser, vocabulary-based
recognizer, hierarchy builder producing normal catalog rows, and plugin host
wiring. No new dependencies.

## Pipeline (`Plugins/SkyCD.Plugin.GCstar`)

- `GCstarParser`: `XmlReader` streaming parse with secure settings (no DTD,
  no resolver, 256 MB cap, depth 256, 1M items). Attributes carry field values
  (exact-name first, case-insensitive duplicate ignored); description-like
  content arrives as child elements. Demo (`*** DEMO ***`) and id-less items
  are skipped with counted per-item reasons. Unknown attributes/elements are
  retained verbatim in field values plus namespaced `GCstar.<name>` extension
  metadata, each reported once in `UnsupportedFields`. Custom exceptions
  (`GCstarMalformedDocumentException`, `GCstarUnsupportedSchemaException`,
  `GCstarEmptyReportException`), one class per file, fixed message templates.
- `GCstarElementNames`: known-field vocabulary, collection markers, image/
  date/flag/free-text classifiers. Dates normalize (`added`, `released`, and
  `*date` fields gain ISO twins); year-only values intentionally gain none.
- `GCstarRecognizer`: bounded vocabulary scan (root `collection` +
  `type`/`version`/`items` attributes + item elements + collection markers),
  rejects SkyCD native XML and generic collection-shaped XML.
- `GCstarHierarchyBuilder`: one `media` root row plus one `file` row per kept
  item (`nodeId`/`parentId`/`kind`/`name`/`sizeBytes`/`properties`), sanitized
  unique node ids, install-size unit conversion, typed multi-value lists.
- `GCstarPlugin`: `IFileFormatPluginCapability` + content-aware detection,
  formats `.gcs`/`.xml`, read-only.

## Tests

`tests/SkyCD.Plugin.GCstar.Tests` (xUnit, synthetic fixtures under
`Fixtures/GCstar`): parser metadata/verbatim values/skip reasons/extension
retention/empty/wrong-root/nested/DTD/truncation/cancellation/depth cases;
builder root/item typing, multi-value splits, sizes, summary, duplicate-id
uniqueness; detection true/false cases; plugin metadata and end-to-end
read success/failure paths.
