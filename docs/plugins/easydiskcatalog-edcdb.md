# Easy Disk Catalog Maker (.edcdb) import plugin

`edcdb` imports Easy Disk Catalog Maker 1.7 catalogs (`.edcdb`) without the original application installed.

## Codec and provider

The database is a SQLite database encrypted with the legacy System.Data.SQLite RC4 codec, so the project's normal SQLite provider cannot open it. Codec support is isolated behind this plugin, which references [SQLite3MC.PCLRaw.bundle](https://www.nuget.org/packages/SQLite3MC.PCLRaw.bundle) version 2.7.1 (MIT license) and opens the file with `cipher=rc4` plus the compatibility password. SQLite3 Multiple Ciphers 2.7.1 is based on SQLite 3.54.1 and ships native libraries for Windows, Linux and macOS, so the plugin works on every CI platform. No `System.Data.SQLite` assembly is required or referenced.

Detection checks the file content, never the extension alone: plaintext SQLite files are validated against the catalog schema without a password, and anything else is tried with the compatibility RC4 settings across common page sizes. Databases are always opened read-only and are never migrated, vacuumed, rewritten or re-keyed.

## Imported data

Imports produce Media/Folder/File rows consumed by the normal catalog loader. Disks become media entries; folders and files link by `id`/`ParentId`/`DiskID` with `0`, `-1`, null and dangling parents treated as disk roots. Names, hierarchy, sizes, attribute flags, timestamps (`yyyy-MM-dd HH:mm:ss`, loan dates `yyyy-MM-dd`), unsigned CRC values, metadata text, categories/locations (as `A / B` paths), contacts and loan history are retained as typed properties or structured extension metadata. `Files.Preview` is resolved against the sibling `<database name> Previews` directory; missing assets degrade to a `missing` preview status instead of aborting the import. Unknown columns are kept as `Extension.<column>` properties and unknown tables are listed per disk, so no catalog information is silently discarded.

## Diagnostics

- Not an Easy Disk Catalog schema (including ordinary unencrypted SQLite files): `EasyDiskCatalogUnsupportedException`.
- Encrypted content that cannot be decoded (wrong password, unsupported page size, corruption): `EasyDiskCatalogDecryptionException`. The compatibility password never appears in messages, logs or telemetry.
- Truncated or unreadable files: `EasyDiskCatalogCorruptException`.

The compatibility password is a fixed application constant, not a user secret, and is only used to unlock the documented format.

## Tests

`tests/SkyCD.Plugin.EasyDiskCatalog.Tests` generates an encrypted fixture catalog independently of the original application (one disk with nested folders/files, attributes, timestamps, CRC, preview/metadata references, categories/locations, one contact with a current and a returned loan, a sibling preview directory with one present and one missing asset, empty/null optionals, an unknown disk type, plus an unknown column and table) and verifies hierarchy, retained fields, preview resolution, source-file integrity (SHA-256 before/after), wrong-password, corrupt-file and unencrypted-input diagnostics, stream imports without a source path, cancellation, and content detection.
