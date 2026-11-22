# abeMeda/NeoFinder import

The `SkyCD.Plugin.FileFormat.AbeMeda` plugin imports abeMeda/NeoFinder data through two read-only
file-format capabilities that share one enable toggle in **Options → Plugins**:

| Format id | Extensions | Source |
| --- | --- | --- |
| `abemeda-xml` | `.xml` | XML catalog/list/find-result exports |
| `abemeda-native` | `.cdw`, `.abeMeda7`, `.neofinder7` | Native abeMeda/NeoFinder/CDFinder catalog container |

Both formats come from the same vendor product family, so they ship as one assembly. SkyCD opens
the right capability through **File → Open** or `skycd open <path>`; `.xml` is disambiguated by
content inspection, and native catalogs are claimed only when the binary signature and header
sentinel match. Export back to abeMeda/NeoFinder is not supported. Discovery uses assembly metadata
and the capability types; no `plugin.json` is involved.

## Plugin identity and packaging

The plugin id is `SkyCD.Plugin.AbeMeda`; the former native id `SkyCD.Plugin.AbeMeda.Native` is
retired. Existing `PluginDocument` rows for the retired id are kept but marked unavailable on the
next discovery pass, and the merged plugin is discovered with its own persisted enabled state
unchanged. The merged project was formerly two assemblies (`SkyCD.Plugin.FileFormat.AbeMeda` and
`SkyCD.Plugin.FileFormat.AbeMeda.Native`); only the `.Native` assembly disappeared, and the native
reader moved to the `Native/` folder of the surviving project.

## XML exports (`abemeda-xml`)

### Schema basis

The vendor XML schema is unpublished, and no redistributable real export was
available: this capability maps element/attribute *variants* reconstructed from the
abeMeda manual (XML export since abeMeda 6.2, including list, hierarchical and
find-results shapes) and the shared NeoFinder/abeMeda catalog concepts (volumes,
folders, files, comments, dates, sizes, media metadata). Committed fixtures are
therefore synthetic and version-labeled; conformance against genuine application
exports is an explicit open follow-up, and the parser accepts a documented
superset of tag/attribute spellings so real files have room to match.

Family roots: `AbeMeda`, `NeoFinder`, `CDFinder`, `CDWinder` (any namespace).
Volumes: `Volume`, `Disk`, `Media`, `Catalogue`, `Archive`, `CatalogVolume`.
Folders: `Folder`, `Directory`, `Dir`. Files: `File`, `Item`, `Track`, `Song`,
`Photo`, `Picture`, `Image`, `Movie`, `Video`, `Document`. Known fields
(case-insensitive, as attributes or child elements): name/title, path, size,
kind/type, created/modified/accessed dates, comment/description, label, keywords.
Everything else entry-level is retained with `AbeMeda.<Element>.<Attribute>` or
`AbeMeda.<Element>` keys and reported once as an unsupported-field diagnostic.

### Imported data

Volumes become media entries; folders/files link by nesting, or by `Path` in flat
find-result exports (parents derived from path segments, duplicates in distinct
paths stay distinct). Sizes parse defensively (negative or unparsable become 0);
dates normalize to ISO-8601 when they match known export formats and are otherwise
kept verbatim. Comments, labels, keywords, export kind, original paths, schema
version and source root travel as properties. Thumbnail/cover payloads are treated
as unavailable: only a `HasThumbnail` presence marker is retained, never blob data.

### Safety and diagnostics

Parsing uses hardened `XmlReader` settings (no DTD, no resolvers), a 256-level
depth cap, a 1,000,000-item cap and a 256 MiB document cap, with cancellation
honored while reading. Wrong roots fail with `AbeMedaUnsupportedSchemaException`,
truncated content with `AbeMedaMalformedDocumentException`, and item-less exports
with `AbeMedaEmptyReportException`. The summary carries volume/folder/file counts,
the import mode (Nested/Flat), skipped rows with reasons, and unsupported fields.

## Native catalogs (`abemeda-native`)

### Scope and status

| Item | Status |
|---|---|
| Content recognition (`DWDRDNBA` signature + header sentinel) | Implemented |
| Fixed header parsing and bounds validation | Implemented |
| 36-byte record table (`BLOB`/artifact records) | Implemented |
| Catalog identity and raw header preservation | Implemented |
| Embedded artifact inventory (handler, flags, ranges) | Implemented as an unassociated folder |
| Item hierarchy, names, comments, labels, ratings | Not decoded |
| Extracted EXIF/IPTC/XMP/ID3 metadata | Not decoded |
| Item-to-artifact association | Not decoded |
| Extended generation (`.abeMeda7`, `.neofinder7`) | Recognized, gated |
| Pre-2.8 legacy catalogs | Not recognized |

The vendor format is unpublished. Only offsets and relationships confirmed from the
shipped `abeMeda Demo Catalog.cdw` (7.8.7 line) are treated as known. Genuine
fixtures for the remaining tables are tracked by issues `#589`, `#590`, and `#591`;
item-level parsing is intentionally absent rather than guessed.

### Formats and generations

- Classic container: `.cdw` (also registered by the family as CDWinder catalogs).
- Extended container: `.abeMeda7` (abeMeda Extended) and `.neofinder7`
  (NeoFinder Extended). NeoFinder 7 introduced a generation break, so the classic
  layout is never applied to these files.
- Recognition is content-based: the file must start with the 8-byte `DWDRDNBA`
  signature and carry the `0xA5A5A5A5` header sentinel at `0x3C`.
- A recognized extended file fails with an actionable diagnostic instead of being
  parsed with the classic layout.
- Pre-CDFinder-2.8 catalogs are not recognized; the vendor converts them with its
  own importer, and they remain a separate generation.

### User import instructions in SkyCD

1. Choose **File -> Open...** (or `skycd open <path.cdw>`).
2. Select the native catalog file. Content-aware detection verifies the signature
   and sentinel before importing.
3. SkyCD imports a media node for the catalog, the preserved header identity, and
   an **Embedded artifacts (unassociated)** folder listing the container records.
4. Source files are opened read-only and are never modified.

### Verified container layout (classic version 105)

All multi-byte values are little-endian. Offsets below are absolute from the start
of the file.

| Offset | Size | Observed value/type | Meaning | Handling |
|---:|---:|---|---|---|
| `0x00` | 8 | `DWDRDNBA` | Signature | Required |
| `0x08` | 4 | `105` | Version candidate | Must equal the supported version |
| `0x18` | 4 | `0xC4E69DE6` | Integrity/identity | Preserved, not verified |
| `0x20` | 4 | `105` | Compatibility/minimum version | Must equal the supported version |
| `0x2C` | 4 | `0x01E72E65` | Unknown | Preserved as raw metadata |
| `0x30` | 4 | `0x0210E067` | Unknown | Preserved as raw metadata |
| `0x34` | 4 | `6` | Unknown count/flags | Preserved as raw metadata |
| `0x38` | 4 | `10` | Unknown count/flags | Preserved as raw metadata |
| `0x3C` | 4 | `0xA5A5A5A5` | Header sentinel | Required |
| `0x40` | 4 | `144` | Header size / first record offset | Must equal `0x90` |
| `0x44` | 4 | `1370` | Declared record count | Bounded, not treated as a file count |
| `0x48` | 4 | `36` | Record stride | Must equal 36 |
| `0x4C` | 4 | `279212` | Section offset | Checked against the file length |
| `0x50` | 4 | `3009998` | Section length | Checked against the file length |
| `0x64` | 4 | `4096` | Block/alignment size | Non-zero and below the configured cap |

The header ends at `0x90`. Records begin there and must fit before the section
offset. The section (`II`/`IN`/`BZ`/`CE`/`BLOB` concepts in internal diagnostics)
is modeled as a separately bounded region; one section's size is never used to
validate another.

#### Entry record layout (36 bytes)

| Relative offset | Size | Field | Handling |
|---:|---:|---|---|
| `0` | 8 | Ordinal/identifier | Must increase strictly; non-increasing records are skipped with a warning |
| `8` | 8 | BLOB size or offset | Preserved verbatim; values beyond the file length are warned |
| `16` | 4 | Flags/type | Preserved verbatim |
| `20` | 8 | Handler/type code (NUL-padded ASCII) | Preserved; non-text handlers are stored as hex with a warning |
| `28` | 4 | Range/offset A | Preserved; outside-file values are warned |
| `32` | 4 | Range/offset B | Preserved; outside-file values are warned |

The shape is an observation, not a stable vendor contract. The importer does not
interpret the BLOB value as a length or the ranges as payload slices, and it never
reads or extracts payload bytes. Handler prefixes `JPEG`, `PNG`, and `GIF` are
mapped to a MIME hint; this is the only interpretation applied.

### Validation and resource bounds

- The source stream must be readable and seekable.
- Every read is checked against the known file length before allocation; 64-bit
  arithmetic is used for section and entry-table bounds.
- At most 1,000,000 records are accepted; the record table must fit before the
  section start.
- Block size must be non-zero and at most 16 MiB.
- Warnings are capped at 1,000 entries.
- Cancellation is honored before header validation and every 256 records.
- Nothing is executed, followed as a reference, or written back.

### Failure diagnostics

Custom exceptions (one class per file) carry predefined templates:

- `AbeMedaNativeUnsupportedSchemaException` — missing signature or unseekable source.
- `AbeMedaNativeUnsupportedVersionException` — unsupported or inconsistent version markers.
- `AbeMedaNativeUnsupportedLayoutException` — header size or record stride differs from the verified profile.
- `AbeMedaNativeUnsupportedGenerationException` — recognized extended catalog.
- `AbeMedaNativeCorruptCatalogException` — truncation, bad sentinel, or out-of-file section/table.

The plugin boundary converts these into `FileFormatReadResult.Error`; cancellation
is rethrown. No generic exception escapes.

### Version matrix

| Generation | Suffix | Container | Status |
|---|---|---|---|
| Classic | `.cdw` | `DWDRDNBA`, version 105 | Header and record layer supported |
| Extended | `.abeMeda7` | Recognized by suffix | Gated pending fixtures |
| Extended | `.neofinder7` | Recognized by suffix | Gated pending fixtures |
| Legacy | pre-2.8 | Unknown | Not recognized |

Support is never claimed from the suffix alone: the signature and sentinel must
match, and the extended generation additionally requires verified fixtures.

### Known unknowns

- Meaning of the integrity field and whether any checksum covers the header.
- Whether `0x44` is a record count, a byte count, or a capacity value.
- Layout of the `II`, `IN`, `BZ`, and `CE` regions and the item/folder tables.
- Item names, paths, parent IDs, comments, labels, ratings, and media metadata.
- BLOB framing, compression, and item association for previews/cover art.
- Differences between classic and extended generations.

These remain blocked on genuine, redistributable fixtures with provenance.

## Tests

`tests/SkyCD.Plugin.FileFormat.AbeMeda.Tests` covers both capabilities in one project.

XML exports: a nested two-volume synthetic export (hierarchy, Unicode, missing attributes,
duplicates, metadata variants, unknown names, thumbnails), a flat path-based export,
recognizer true/false cases with position preservation, malformed/empty/wrong-root
diagnostics, cancellation, and the import summary.

Native catalogs: synthetic containers built at runtime from the documented offsets; no vendor
bytes are committed or redistributed. The suite covers recognition and position preservation,
classic import, empty catalogs, unknown header preservation, duplicate/non-increasing ordinals,
non-text handlers, out-of-file ranges and blob values, unsupported versions and layout
constants, truncated headers, bad signatures, extended-generation gating, cancellation,
progress reporting, read-only writes, and persistence/reopen through
`MainWindow.ExtractCatalogEntries` plus Couchbase Lite.
