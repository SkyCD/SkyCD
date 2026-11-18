# abeMeda XML export import plugin (`abemeda-xml`)

Read-only capability in `SkyCD.Plugin.AbeMeda` that imports abeMeda/NeoFinder-family
XML catalog exports. Discovery uses assembly metadata and the capability type; no
`plugin.json` is involved.

## Schema basis

The vendor XML schema is unpublished, and no redistributable real export was
available: this plugin maps element/attribute *variants* reconstructed from the
abeMeda manual (XML export since abeMeda 6.2, including list, hierarchical and
find-results shapes) and the shared NeoFinder/abeMeda catalog concepts (volumes,
folders, files, comments, dates, sizes, media metadata). Committed fixtures are
therefore synthetic and version-labeled; conformance against genuine application
exports is an explicit open follow-up, and the parser accepts a documented
superset of tag/attribute spellings so real files have room to match. Native
catalog fixtures for the same family are tracked separately in the
[issue #589 fixture acquisition note](../implementation/issue-589-abemeda-native-fixtures.md).

Family roots: `AbeMeda`, `NeoFinder`, `CDFinder`, `CDWinder` (any namespace).
Volumes: `Volume`, `Disk`, `Media`, `Catalogue`, `Archive`, `CatalogVolume`.
Folders: `Folder`, `Directory`, `Dir`. Files: `File`, `Item`, `Track`, `Song`,
`Photo`, `Picture`, `Image`, `Movie`, `Video`, `Document`. Known fields
(case-insensitive, as attributes or child elements): name/title, path, size,
kind/type, created/modified/accessed dates, comment/description, label, keywords.
Everything else entry-level is retained with `AbeMeda.<Element>.<Attribute>` or
`AbeMeda.<Element>` keys and reported once as an unsupported-field diagnostic.

## Imported data

Volumes become media entries; folders/files link by nesting, or by `Path` in flat
find-result exports (parents derived from path segments, duplicates in distinct
paths stay distinct). Sizes parse defensively (negative or unparsable become 0);
dates normalize to ISO-8601 when they match known export formats and are otherwise
kept verbatim. Comments, labels, keywords, export kind, original paths, schema
version and source root travel as properties. Thumbnail/cover payloads are treated
as unavailable: only a `HasThumbnail` presence marker is retained, never blob data.

## Safety and diagnostics

Parsing uses hardened `XmlReader` settings (no DTD, no resolvers), a 256-level
depth cap, a 1,000,000-item cap and a 256 MiB document cap, with cancellation
honored while reading. Wrong roots fail with `AbeMedaUnsupportedSchemaException`,
truncated content with `AbeMedaMalformedDocumentException`, and item-less exports
with `AbeMedaEmptyReportException`. The summary carries volume/folder/file counts,
the import mode (Nested/Flat), skipped rows with reasons, and unsupported fields.

## Tests

`tests/SkyCD.Plugin.AbeMeda.Tests` covers a nested two-volume synthetic export
(hierarchy, Unicode, missing attributes, duplicates, metadata variants, unknown
names, thumbnails), a flat path-based export, recognizer true/false cases with
position preservation, malformed/empty/wrong-root diagnostics, cancellation, and
the import summary.
