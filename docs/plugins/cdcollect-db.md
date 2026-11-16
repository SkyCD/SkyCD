# CDCollect SQLite catalog plugin

`cdcollect-db` imports CDCollect schema-version-4 SQLite 3 catalogs (`cds3.db`, `*.db`, `*.cdcollect`) and exports version 4. Legacy version-0 `files` catalogs remain readable. Other versions fail with an actionable dedicated exception; malformed version rows and missing columns are rejected.

Detection checks the SQLite signature and required catalog columns, including the version column for `files_v1`. An extension alone never identifies a catalog. Nonseekable detection returns false; import supports nonseekable streams.

Imports produce Media/Folder/File rows consumed by the normal catalog loader. Names, hierarchy, sizes, dates, keywords, icons, exact information text, MIME types, locations, and packed markers are retained. Additional information fields are extracted into properties. When SourcePath is supplied, available PNG sidecars in `thumbs/<media-id>/<media-id>_<tree-path>.png` are retained as a `Thumbnail` data URI property (up to 8 MiB each). Thumbnail paths only use numeric catalog indices; stored file locations never select sidecars.

Export accepts catalog row arrays, domain document arrays, JSON arrays, and imported reports. Media IDs and colon-separated sibling indices are assigned in input order. Media must be roots; missing parents, duplicate IDs, cycles, nested media, negative sizes, and files exceeding CDCollect 0.6.x's signed 32-bit size reader are rejected before the output stream is written. Network folders/resources become directories. Missing dates become zero components. Info, Comments, then Description supply file information; Keywords then Comments supply media keywords. Unmapped SkyCD properties and IDs are omitted; directory sizes are recalculated on import. Export writes the database stream only: thumbnails remain available in SkyCD properties but are not exported as sidecar files.

Executable `infoplugins` and `compplugins` rows are only counted on import, never invoked or restored into executable configuration. Export creates these tables empty. SQLite connections use read-only mode for input and disable pooling. Discovery uses the existing assembly capability; no manifest file is involved. No new dependencies are introduced.

The implementation independently expresses observed schema/format facts from CDCollect SVN r138 (DBsql.cs, Conf.cs, FileItem.cs); no GPL implementation is copied. Committed synthetic fixtures document provenance in the test Fixtures directory. Automated tests verify schema and round trips; a CDCollect 0.6.x GUI smoke test requires a compatible Mono/GTK installation.

Legacy SQLite 2 `cds.db` catalogs use the separate [read-only SQLite 2 capability](cdcollect-sqlite2.md) from this assembly.
