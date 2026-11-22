# NeoFinder text-export fixtures

No genuine NeoFinder export could be redistributed with this repository, so the
test suite generates synthetic exports at runtime (see `NeoFinderTextFixture`).
The synthetic layouts mirror the documented **Text with Tab** export:

- Delimiter: tab (comma and semicolon variants are covered too).
- Headers included, all levels, one row per item.
- Columns: Name (always present), Kind, Size, Where, Volume, Catalog,
  Date Modified, Comments, plus localized and edge-case variants.
- macOS path styles (`/` and classic `:` separators), Unicode names in
  composed form, locale-dependent dates (`yyyy-MM-dd HH:mm:ss` and
  `dd.MM.yyyy HH:mm:ss`), quoted fields and multi-catalog files.

Recommended export configuration (File -> Export in NeoFinder, one or more
catalogs selected):

1. Format: **Text with Tab**.
2. Check **include column headers**.
3. Levels: **All**.
4. Include at least **Name** plus one of **Where/Volume/Catalog** so SkyCD can
   reconstruct the hierarchy; also include Kind, Size, dates and Comments for
   full metadata.

See `docs/plugins/abemeda.md` for supported versions, encodings and the
column mapping.
