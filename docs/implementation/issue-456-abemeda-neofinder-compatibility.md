# Issue #456: Investigate shared abeMeda/NeoFinder native catalog compatibility

## Verdict: no-go for native import at this time

A native importer would have to parse an unpublished proprietary container:
per-volume catalog files with no documented signature, magic, schema, or SDK,
spanning at least two format generations plus a legacy line that even the
vendor only converts through its own Importer. No redistributable fixtures
could be obtained, so a native codec could not be tested against genuine files.
The existing `abemeda-xml` (XML export import) and `neofinder-text`
(configurable text export import, #454) plugins already cover the physical
hierarchy, names, sizes, dates, comments, and labels. Revisit only with (a)
genuine fixtures from identified application versions and (b) a known,
license-compliant container access path.

## Evidence

All findings below come from reading vendor documentation (fetched 2026-11-17;
nothing downloaded except web pages, nothing executed) and the in-repo
`AbeMeda` and `NeoFinder.Text` plugins. No native catalog file was obtained:
no sample, trial-bundled, or user-donated catalog from any abeMeda / NeoFinder
/ CDFinder / CDWinder version could be found, so no container bytes, schema
dump, or version marker was ever observed. Every statement about native
internals is therefore docs-derived, not file-observed, and is labeled as such.

Vendor sources:

- [abeMeda compatibility FAQ](https://www.abemeda.com/en/en/faq.html) —
  shared format statement, database-folder move/backup, thumbnail behavior,
  PDF/video/archive helpers.
- [NeoFinder export guide](https://cdfinder.de/guide/10/export_catalogs.html) —
  Text with Tab / Indented Text / XML export shapes.
- [Transfer catalogs between NeoFinder Mac and abeMeda for
  Windows](https://www.cdfinder.de/guide/2/2.10/across_platforms.html) —
  interchangeability, single-catalog and whole-database transfer.
- [Install NeoFinder and abeMeda in a
  network](https://www.cdfinder.de/guide/2/2.3/neofinder_cross_platform.html) —
  shared data format, shared network folder, filename constraints.
- [Backup your NeoFinder
  database](https://www.cdfinder.de/guide/4/4.5/neofinder_backup.html) —
  one file per catalog, native suffixes.
- [NeoFinder FAQ](https://www.cdfinder.de/guide/25/faq.html) — CDFinder 2.8
  Importer conversion, NeoFinder-adopts-CDFinder-database upgrade path.
- [Add additional information to
  catalogs](https://www.cdfinder.de/guide/4/4.3/extra_fields.html) — comments,
  extra fields, custom fields, color labels.
- [Catalog "cloned"
  disks](https://www.cdfinder.de/guide/3/3.11/catalog-cloned-disks.html) —
  volume identity including `.neofinder.abemeda.volinfo.xml`.

### Container and format (docs-derived, never file-observed)

- One native catalog file is created per cataloged volume or folder, stored in
  a single configurable database folder, and named after the volume or folder
  it catalogs. Default locations are `~/Library/Application Support/NeoFinder/
  NeoFinder Database/` on macOS and `C:\Users\<name>\Documents\abeMeda Catalog
  Library` on Windows (both relocatable via Preferences; revealed via
  Reveal-in-Finder / Show-catalog-file).
- Native filename suffixes are `.neofinder`, `.neofinder7`, `.cdw`, and
  `.abemeda7` (all four must be recognized by NeoFinder iOS, which confirms
  all four as live native suffixes, not just history).
- NeoFinder 7 introduced the Extended Catalog File Format with the
  `.neofinder7` suffix (mirrored by `.abemeda7` on Windows): a documented
  generation break. Catalogs created by CDFinder 2.8 or earlier are NOT read
  directly — the vendor directs users to its Importer to convert that old
  format. So "the shared family format" is at least three generations
  (pre-2.8 legacy, classic `.neofinder`/`.cdw`, extended `*7`), and
  interoperability must never be generalized across all historical catalogs.
- No signature, magic number, container type, DDL, or SDK is published. The
  container may be SQLite-based, Apple Core Data-based, or fully proprietary;
  none of the vendor pages say. Content sniffing without a compliant reader is
  therefore impossible, and support must never be claimed from the extension
  alone — the exact failure mode #455 identified for `.vvv`.
- Vendor-confirmed interop (current generations only): abeMeda and CDFinder /
  NeoFinder "share the same catalog file format ... like JPEG files — you
  don't import them, they just work, wherever you are"; both sides read, write
  and manipulate catalogs regardless of which application saved them last.
  Whole-database moves, single-catalog copies, and shared network folders (the
  latter requiring a Business license on each seat) are file copies, never
  conversions. NeoFinder adopts an existing CDFinder database folder in place
  on upgrade.
- Cross-platform filename constraint: NeoFinder avoids Windows-illegal
  characters when naming catalogs, and catalog display names may differ from
  catalog file names.

### Schema and versioning (docs-derived, never file-observed)

- Identity: catalogs are identified by volume/folder name, backed by volume
  size, volume creation date, and an invisible
  `.neofinder.abemeda.volinfo.xml` marker file written at the root of the
  cataloged disk (disableable via a defaults flag). This is the native answer
  to volume/file/folder identity: stronger than anything the text/XML exports
  carry.
- Per-item data: every item carries a comment; catalogs additionally carry
  seven extra fields (e.g. Location for physical shelf position) plus five
  user-nameable custom fields (names configured in Preferences); color labels
  mark items; photos can be rated. None of these field sets appear in the
  documented text-export column list beyond Name/Where/Volume/Catalog/Kind/
  Size/dates/Comments/label.
- Extracted metadata cataloged from file contents: image size, color depth,
  EXIF, IPTC, Adobe XMP, ID3, audio cover art and audio previews, video codec
  data. PDF metadata/thumbnails/content arrive via an Xpdf helper; video
  thumbnails/metadata via ffmpeg, QuickTime, or a DirectShow codec pack; extra
  archive content via 7-Zip. Several of these helpers are external tools the
  user installs and points the application at — a native importer must never
  execute or shell out to them.
- Thumbnails are embedded in the catalog file: the vendor tells users watching
  a large photo catalog build to "look at the catalog file size" to see what
  accumulates, and warns that thumbnail resolutions above an internal ~300 px
  threshold switch to a costlier, better-looking generator. Embedded vs.
  external is therefore settled for the image/video/PDF case (embedded blobs
  of unknown encoding inside the proprietary container), which defines the
  portable-extraction requirement: bounded, presence-first blob reads with no
  original-media access and no helper execution. Blob framing cannot be
  designed until real files are observed.

### What the XML/text exports carry (in-repo, verified against docs)

- Text with Tab export (any license): configurable columns; Name is always
  included; Where / Volume / Catalog rebuild hierarchy (POSIX and classic Mac
  OS `:` separators, multi-catalog grouping); Kind, Size (raw or
  unit-suffixed), locale-formatted dates normalized by the importer,
  Comments, label. Indented Text is a human-readable tree dump the importer
  deliberately does not support. XML export additionally needs a Business
  license and ships list, hierarchical, and find-results shapes.
- Not exported, i.e. the native-only prize: embedded thumbnails/previews/cover
  art (importers keep a `HasThumbnail` presence marker only), extracted
  EXIF/IPTC/XMP/ID3 detail beyond the mapped columns, catalog extra/custom
  fields beyond comments, color labels and ratings, volume identity
  (size/creation-date/volinfo marker), internal IDs and format version.
- The in-repo `abemeda-xml` plugin maps element/attribute *variants*
  reconstructed from the abeMeda manual (XML export exists since abeMeda 6.2);
  committed fixtures are synthetic and version-labeled, and conformance
  against genuine application exports is an explicit open follow-up
  (`docs/plugins/abemeda-xml.md`). The `neofinder-text` plugin (#454) is the
  same story for text exports (`docs/plugins/neofinder-text.md`).

## Compatibility matrix

| Data | Text/XML today | Native potential | Notes |
|---|---|---|---|
| Physical tree, names, sizes, dates, comments, labels | Yes | Yes | Core catalog data already imports |
| Volume identity (size, creation date, volinfo marker) | No | Yes | Robust re-catalog/de-dupe matching |
| Catalog extra fields (7) + custom fields (5) | No (comments only) | Yes | Shelf/location workflows live here |
| Color labels, photo ratings | No | Yes | Niche but real user data |
| Extracted metadata (EXIF/IPTC/XMP/ID3, video codec) | Columns only | Yes | Second main benefit of native |
| Embedded thumbnails/previews/cover art | Presence marker only | Yes, bounded | Main structural benefit; blob framing unknown |
| Internal IDs / format version | No | Yes | Diagnostics, not user data |
| Legacy pre-2.8 catalogs | Via vendor Importer only | Separate task | Must not be generalized into one reader |

## Why native is not justified now

1. **No known compliant access path.** The container is unpublished with no
   SDK and no observable magic. Any reader starts with clean-room reverse
   engineering of a proprietary format — high cost, fragile across the three
   known generations, and subject to the vendor's license terms, which were
   not reviewed for reverse-engineering permission as part of this
   investigation.
2. **No verifiable fixtures.** Targeted web searches surfaced no
   redistributable, trial-bundled, or documented sample catalogs in any of the
   four suffixes; only the applications themselves (commercial downloads) and
   user-guide pages. A native codec written against zero genuine files is
   untestable by construction.
3. **Version drift without a reader.** Extended (`*7`) vs. classic
   (`.neofinder`/`.cdw`) vs. pre-2.8-legacy (Importer-converted) means one
   "family reader" is really a version-gated reader family with per-generation
   fixtures and regression files.
4. **Blob handling is undesigned.** Embedded thumbnails make unbounded reads a
   real hazard (hundred-thousand-file volumes exist per the vendor FAQ); the
   bounded-extraction design needs real blob framing first.
5. **Unknown-license dependency risk is currently moot but reserved.** If the
   container ever proves to be SQLite-based, the in-repo precedent
   (`Plugins/SkyCD.Plugin.Katalog.Sqlite` via `Microsoft.Data.Sqlite`) offers
   a plausibly compliant path under the existing
   `docs/legal/dependency-license-policy.json`; anything else (Core Data
   bridges, proprietary SDKs, helper binaries) re-opens the license question,
   and policy fails closed on unknown licenses.

## Proposed bounded follow-ups (only if the blockers lift)

- Fixture task per generation: obtain application-created catalogs from
  identified abeMeda/NeoFinder/CDFinder versions (user-donated copies carry
  provenance best), one classic and one extended at minimum, plus — if
  obtainable — a pre-2.8 legacy file; store under
  `tests/SkyCD.Plugin.<Name>.Tests/Fixtures/` with provenance notes. Until
  then, extend the XML/text plugins' locale and layout coverage instead.
- Recognition spike once bytes exist: magic/marker sniffing for the four
  suffixes with position preservation, reusing the content-aware recognizer
  pattern; never claim support from the extension alone.
- Native reader spike only after fixtures plus container identification:
  read-only, per-generation gated, unknown/corrupt-version diagnostics,
  presence-first thumbnail handling with byte and count bounds, no media
  access, no helper execution; keep the export plugins as the offline
  fallback.
- Keep documenting recommended export settings (Text with Tab, headers on,
  Levels: All) — already covered in `docs/plugins/neofinder-text.md`.

## Limits honoured

No catalogs were obtained, so no originals existed to modify; no embedded
content was executed and no external references were followed (only vendor
documentation pages and in-repo plugin sources were read); no upstream or
third-party code was copied; no support is claimed from file extensions; this
research required no native export support and proposes none.
