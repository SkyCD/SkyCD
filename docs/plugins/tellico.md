# Tellico Collection Import Plugin

## Overview
The `tellico` plugin provides import-only support for Tellico collections:
native `.tc` archives (a ZIP holding `tellico.xml` plus an `images/`
folder; legacy `bookcase.xml` documents are also accepted) and plain Tellico
XML exports (`.xml`). Document shape follows the published Tellico DTD
(v11; syntax version 12 adds filter-rule values only). Collection items
import as `entry` rows — never as files — and the catalog loader maps the
`entry` kind explicitly.

## User Import Instructions in SkyCD
1. In Tellico, save your collection (`.tc`, the native format) or use
   File -> Export -> Tellico XML for a plain `.xml` file.
2. In SkyCD, choose **File -> Open...** and select the file.
3. Content-aware detection recognizes Tellico collections automatically.
4. Each collection becomes one media node (named after the collection
   title) with one `entry` row per item. Source files are never modified.

## Supported Versions & Variants
- **Syntax versions**: 4 through the verified 12. Newer versions import
  tolerantly with a warning; older ones are rejected. Missing or
  unparseable versions proceed with a warning.
- **Containers**: `.tc` ZIP archives (document entry plus `images/`
  members) and standalone Tellico XML. Other ZIPs and other XML are
  rejected with actionable errors.
- **Collections**: all types (`type` 1–13) import through the same generic
  path; the raw type id is preserved as `TellicoCollectionType`. Multiple
  collections per file are supported.
- **Encodings**: XML-declared encodings (UTF-8 expected).

## Field Mapping
- Field definitions come from the document's own `fields` section: text
  (`Line`, `Para`, `URL`, `Choice`), canonical numbers (`Number`,
  `Rating`), normalized booleans, ISO dates plus raw twins (`Date`,
  including structured year/month/day elements), image references,
  multi-value lists from repeated elements (`tracks/track`,
  `authors/author`, ...) and from semicolon-separated `AllowMultiple`
  values. Unknown-typed fields import as text with a diagnostic.
- Identity: `entry id` attributes (provisional `entry-N` ids assigned with
  a counted note when missing). Display names prefer `title`, then `name`,
  then the first scalar field.
- Images: `images` section entries map by id with format and dimensions;
  entry image fields resolve to `ImageId`/`ImageFormat`/`ImageDimensions`
  properties. Image *bytes* are not imported (bounded-memory decision);
  only metadata travels with the row.
- Loans: `borrowers/borrower/loan` entries attach `Borrower`, `LoanDate`,
  `DueDate`, `LoanUid` to the referenced entry; dangling references are
  reported, not fatal.
- Unknown elements are retained verbatim as `Tellico.<name>` extension
  metadata and reported once. Filters, macros, and bibtex preambles are
  not imported and are listed as unsupported. Field properties (e.g.
  bibtex-field names) are noted, not mapped.

## Security and Resource Bounds
- DTD validation and all external resolution are disabled
  (`XmlResolver = null`); entity expansion is bounded, so the remote DTD
  reference Tellico files carry is never fetched and billion-laughs
  payloads fail closed.
- Archives are guarded: entry-count, per-entry, and total-size caps;
  absolute paths, drive letters, and `..` segments are rejected without
  extracting anything.
- Parsing is streaming with bounded row/field growth; progress and
  cancellation are honored.

## Known Loss of Information
- **Image bytes**: metadata only; use Tellico itself to view originals.
- **Filters/saved searches, macros, bibtex preambles**: structural export
  aids, listed as unsupported.
- **Field properties** (e.g. bibtex-field names): noted, not mapped.
- No remote lookups or cover downloads happen during import.
