# Legacy CDCollect SQLite 2 catalogs

The CDCollect assembly discovers `CdCollectSqlite2Plugin` as a separate, read-only file-format capability (`cdcollect-sqlite2`). It imports pre-0.6 `cds.db` catalogs through the [standalone SkyCD.Native.Sqlite2 NuGet package](https://github.com/SkyCD/SkyCD.Native.Sqlite2), which bundles the SQLite 2.8.17 native engine. Users need no SQLite 2 installation or conversion executables. SQLite 3 catalogs continue to use `cdcollect-db`. The legacy capability is absent from save/export choices.

Detection requires the SQLite 2 signature, a valid native integrity check, and CDCollect's `cds` and `files`/`files_v1` column definitions. The extension alone is insufficient. Detection preserves stream position; import also supports nonseekable streams and short reads.

Supported schemas:

- Unversioned or explicit version-0 `files` catalogs, with `cds(id,name,keywords,year,month,day,icon)` and `files(cd_id,path,name,info,size,mime,type,packed)`.
- Version-4 `files_v1` catalogs stored in SQLite 2, with the additional `location` column.
- Columns can be reordered; unrelated columns are ignored. Column declarations must use CDCollect's simple layout. Missing required columns, unsupported layouts, invalid version rows, and intermediate or unknown versions raise `CdCollectSqlite2VersionException` with the upstream migration path.

The plugin keeps media names, hierarchy, dates, keywords, icons, exact information text, sizes, MIME types and packed/archive markers. It writes a private SQLite 3 staging catalog with fixed statements and parameterized values, then reuses SkyCD's existing domain mapping. Available thumbnail sidecars can be imported when the original source path is supplied. Executable-plugin configuration is never invoked or migrated; no SQLite 2 files are exported.

The native package opens a private snapshot, restricts SQL through an authorizer, and checks integrity before table reads. Its native bounds patches reject cyclic pages/cells and excessive depth. Input is bounded to 256 MiB, decoded records to 16 MiB, columns to 128, rows to one million per query and native b-tree depth to 64. Cancellation is checked during copying, native VM execution and mapping. Non-UTF-8 text is rejected. The original catalog remains unchanged.

Committed synthetic fixtures were generated and independently integrity-checked using the official SQLite 2.8.17 CLI. Tests cover both byte orders, domain persistence, unsupported variants, corrupt/truncated data and native corruption defenses. No CDCollect GUI validation is claimed. No GPL implementation or development compiler/tool executable is shipped.

NuGet alternatives were evaluated: `SqliteReader` 1.1.0, `MBW.Utilities.ManagedSqlite` 0.13.0 and `Microsoft.Data.Sqlite` 11.0.0 all read the SQLite 3 control but reject both SQLite 2 fixtures. The native engine replaces the original custom managed parser.

References: [SQLite release source](https://github.com/sqlite/sqlite/tree/version-2.8.17), [CDCollect source and migration FAQ](https://svn.code.sf.net/p/cdcollect/code/cdcollect/trunk/).

The native engine build, interop implementation, sources, runtime artifacts and tests live in [SkyCD/SkyCD.Native.Sqlite2](https://github.com/SkyCD/SkyCD.Native.Sqlite2). SkyCD consumes its CI-published package from [GitHub Packages](https://github.com/orgs/SkyCD/packages/nuget/package/SkyCD.Native.Sqlite2); the upstream source ZIP and compiled native files are not vendored in this repository. `NuGet.Config` maps only this exact package ID to the organization feed. GitHub Actions uses `packages: read` and its token; local developers should supply a `read:packages` credential through their NuGet credential provider or `NuGetPackageSourceCredentials_skycd`, as documented in the package repository. No credentials are committed.

The package's MIT/public-domain license expression is covered by the explicit `SkyCD.Native.Sqlite2` policy exception; its repository includes all native dependency notices and checks its own native SBOM.
