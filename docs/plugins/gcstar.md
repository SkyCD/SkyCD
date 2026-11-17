# GCstar Collection Import Plugin

## Overview
The `gcstar` plugin provides import-only support for collection exports saved by
**GCstar** (the `.gcs` XML format, all collection models). GCstar saves each
collection as a flat `<collection>` document: field values travel as item
attributes while description-like content (`description`, `comments`, `secrets`)
travels as child elements. The plugin recognizes exports by content vocabulary,
not by file extension, so `.gcs` files and renamed `.xml` copies are both
accepted while SkyCD's own native XML catalog and other tools' XML are rejected.

## User Export Instructions in GCstar
No special export step is needed: GCstar's native save format is the import
source. In GCstar open your collection and use **File -> Save** (or Save As);
the resulting `.gcs` file imports directly into SkyCD.

## User Import Instructions in SkyCD
1. In SkyCD, choose **File -> Open...**.
2. Select your exported `.gcs` (or renamed `.xml`) file.
3. Content-aware detection recognizes the GCstar collection automatically.
4. The collection becomes one media node (named from the export's information
   block, or "GCstar Collection") with one leaf entry per item. Source files
   are never modified.

## Supported Versions & Models
- **Software**: GCstar 1.x collection saves (validated against the documented
  1.6-era schema shape; the `type`/`version`/`items` root attributes are read
  but any model name and version are accepted).
- **Models**: all 18 collection models import through the same generic path
  (movies, books, music, games, stamps, coins, wines, and the rest), because
  every model shares the attribute-plus-description-element document shape.
  Model-specific fields outside the known vocabulary are retained verbatim as
  `GCstar.<name>` extension properties and reported as unsupported fields.
- **Encodings**: UTF-8 (with or without BOM) and any encoding declared in the
  XML declaration.

## Field Mapping
- Identity: `id` (required; items without one are skipped with a reason) and
  the display name from `name`, falling back to `title`, then `Unnamed <id>`.
- Lending: `borrower` and `lendDate` map directly; `borrowings` (the
  multi-borrower loan history) is kept verbatim because its triple-list
  separator layout is unverified, so no composite entry is synthesized from it.
- Provenance: `tags` splits on commas into a multi-value list; `description`,
  `comments`, and `secrets` import as long text; cover art fields (`cover`,
  `boxpic`, images, screenshots) are kept as stored paths with the first
  available one also surfaced as `Image`.
- Typing: plain numbers stay numeric (leading zeros are never stripped, so
  identifiers such as EANs keep their text form); flag fields (`case`,
  `manual`, `exclusive`, `favourite`) become `"true"`/`"false"`; date-like
  fields keep raw text plus an ISO `…Normalized` twin when parsing succeeds;
  ` / `-separated values become multi-value lists except in free-text fields.
- Demo placeholders: items carrying the unregistered-version
  `*** DEMO ***` marker are skipped with a reason instead of being imported.

## Security and Resource Bounds
- DTD processing and external entity resolution are disabled
  (`DtdProcessing.Prohibit`, no `XmlResolver`); DTD/entity attacks are rejected.
- Parsing resources are bounded (256 MB document cap, maximum nesting depth of
  256, maximum of 1,000,000 items); exceeded bounds produce descriptive errors.
- Nested `<item>` elements are rejected; unknown elements and attributes are
  retained, never silently dropped.

## Known Loss of Information
- **Thumbnails and binaries**: exports carry image paths, not image data.
- **Loan-history structure**: `borrowings` is preserved verbatim only.
- **Collection hierarchy**: GCstar collections are flat lists; every item
  imports as a direct child of the collection media node.
