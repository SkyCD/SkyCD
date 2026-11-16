# SQLite 2 fixture provenance

All databases are synthetic test data authored for SkyCD under the repository license. No upstream CDCollect database, user data, GPL source, or executable is included.

Generated using the official public-domain [SQLite 2.8.17 Windows CLI](https://www.sqlite.org/sqlite-2_8_17.zip). Format reference: [SQLite 2.8.17 source](https://www.sqlite.org/sqlite-2.8.17.tar.gz). `catalog.sql` is the basic data recipe; invoke the CLI with its database filename as an argument and pass SQL through standard input. Fixture generation is not part of production imports.

Every database below was opened with SQLite 2.8.17 and returned `ok` from `PRAGMA integrity_check`. This independently verifies actual SQLite 2 binary compatibility, rather than relying on a custom test writer. No CDCollect GUI validation is claimed.

- `cds.db`: unversioned legacy `files` schema with media dates, keywords/icons, nested file, packed archive child, exact information whitespace and inert external-command rows.
- `version0.db`: adds `cdcollect_version(ver)` containing 0.
- `files-v1-v4.db`: the `files_v1` layout with `location` and version 4; imports successfully.
- `files-v1-v1.db`: same columns with intermediate schema version 1; dedicated migration error.
- `unsupported-columns.db`: legacy file table with an unrecognized packed-marker column; dedicated schema error.
- `reordered.db`: identical supported metadata with media/file columns reordered and INTEGER PRIMARY KEY at the end.
- `large.db`: base catalog plus a folder and 140 children, including 1,100-byte and 70,000-byte information values. Exercises internal b-tree pages, long overflow chains and all record-index widths.
- `other.db`: a valid non-CDCollect SQLite 2 database.
- `big-endian.db`: `cds.db` with native 16/32-bit b-tree/header/free-list fields byte-swapped; record offset arrays and rowid keys retain their defined byte order. Independently verified by the same SQLite 2 engine, including media IDs/names.

The schema and hierarchy/type-marker facts match observed CDCollect SVN r138 and earlier `files` schemas. Only format facts were used; no GPL implementation was copied. Truncation, cell/page/overflow corruption, and unsupported storage versions are generated from these fixtures in tests.

SHA-256 hashes:

- `big-endian.db`: `3f4eb5ad523f33873b46ace93d0aa9f9b2e56f8c5f14b563e13776f975962178`
- `cds.db`: `7462414d98864a2a8e69eec5947e4a5425785f40ffcc4ccc10a84db01bfbddfb`
- `files-v1-v1.db`: `0f074d2a49614362ca6a83e07fe70f4d1a2fb3cb3897f853606536fb06935ee4`
- `files-v1-v4.db`: `704994548ae756f7379944a77e8d9894cb5419a8370b5e28a50efcae7dbc533d`
- `large.db`: `ab4f043b37a70becaa387477be7fe17316be269215c7bd8d9e20211f0c957599`
- `other.db`: `0c9903037ce207c836d932734e1eae29447dec8a3f21bc435dd46b2386f57732`
- `reordered.db`: `f42d7178cd031cb4e47ed1f6cf7a99c7fd95732166de1d4dbea61b3211563eb0`
- `unknown-version.db`: `32de42d5829a3f7f43c6bdba23a6bef66e16223f4c1cc128402c800b8647d73e`
- `unsupported-columns.db`: `89f967bc545ca053d7b7b6f17e68c56f3d7e25b41d84fae0dc1d9a77c261874c`
- `version0.db`: `b6cf2847072abdba60d87fdf8c1bdaeac449d37f4aad99f23adef20469bf33d7`
