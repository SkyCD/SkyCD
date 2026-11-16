# SkyCD.Native.Sqlite2

.NET 10 wrapper and bundled native **SQLite 2.8.17** engine. This subproject owns platform loading, read-only native access, source snapshots, cancellation, and the reproducible native build. It has no dependency on CDCollect or the plugin system. `SkyCD.Plugin.CdCollect` owns catalog schema validation and domain mapping.

`Sqlite2Database` copies the supplied stream into a private temporary directory and opens that copy with SQLite 2. The original file is never opened for writing. A native authorizer allows only reads, selects and the integrity check; writes, attach and other pragmas are denied. Identifiers are validated before table reads. The SQLite VM progress callback checks cancellation and an operation budget. A native integrity check runs before records are returned. Bounds include 256 MiB input, 16 MiB decoded records, 128 table columns, one million rows per query, and 64 native b-tree levels. Text must be UTF-8.

The custom C# b-tree/record parser has been removed. The native source uses two portability adaptations (64-bit pointers and modern MinGW `off_t`) and narrowly scoped `initPage` bounds checks. The bounds checks were required by the corrupt-fixture tests: stock SQLite 2 can loop on an ancestor page and overrun its cell array on a cyclic cell list. They return `SQLITE_CORRUPT` instead. All changes are visible in the build script; the upstream archive is retained unchanged.

The library is loaded by an explicit path relative to this assembly, under `runtimes/<rid>/native/`, with the unique name `skycd_sqlite2`. It coexists with SQLite 3. Libraries and third-party notices copy transitively into build and publish outputs. Bundled runtimes are `win-x64`, `linux-x64` (glibc 2.17+), `osx-x64` (10.13+) and `osx-arm64` (11+). Unsupported architectures fail with an actionable runtime error.

Rebuild using [Zig 0.15.2](https://ziglang.org/download/0.15.2/) and PowerShell 7:

```powershell
./tools/scripts/native/build-sqlite2.ps1 -ZigExecutable /path/to/zig
```

The script checks the compiler version and SHA-256 of the committed [official SQLite generated source archive](https://www.sqlite.org/sqlite-source-2_8_17.zip), builds all four libraries, and emits their hashes. It performs no downloads. Set `-Runtime` to a subset or `-OutputRoot` to an isolated verification directory. `native/sha256.json` records the shipped hashes. Ordinary .NET builds do not require a C compiler.

SQLite's public-domain notice, MinGW notices, Zig MIT license and LLVM exception notice are included in `native/`. `docs/legal/sqlite2-native-sbom.json` describes native components, checked by CI in addition to the NuGet SBOM. The SQLite2 and MinGW public-domain/ZPL components have explicit package-scoped exceptions in the dependency policy; blocked licenses remain blocked. The system C runtimes are not bundled. Development compiler/tool binaries are not included.

Unit tests cover engine version/loading, both byte orders, rowids, write/attach rejection, cancellation, disposal and unchanged source bytes. CDCollect integration tests exercise complete imports, historical variants, multi-page/overflow records, malformed input and persistence.
