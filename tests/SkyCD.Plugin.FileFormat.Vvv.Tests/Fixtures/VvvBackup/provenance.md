# VVV backup fixtures

## VVV-1.5-fixture-backup.vvvbk

- Produced by: Virtual Volumes View (VVV) 1.5, File -> Backup, on Windows.
- Produced on: 2026-11-18.
- Catalog content: a new catalog with volume `Fixture volume` cataloging a
  directory containing `hello.txt` (39 bytes) and `folder/nested.txt`
  (22 bytes).
- Size: 41,472 bytes.
- SHA-256: `31739c79194b245a06f2fa1dcdd557c19c48e514982434daff67ca681c09706f`.
- Format: Firebird logical backup (gbak stream). Database schema version
  `SERVICE.DB_VERSION = 23`.
- Restore was verified with the Firebird 5 embedded engine; the source file
  was not modified.
- No VVV source code was used to produce this fixture or the importer.

## VVV-fixture-schema-999.vvvbk

- Derived negative fixture, not a genuine VVV output.
- Produced by restoring `VVV-1.5-fixture-backup.vvvbk` with Firebird 5,
  setting `SERVICE.DB_VERSION = 999`, and creating a new logical backup.
- Used only to verify rejection of unsupported schema versions.
- Size: 48,128 bytes.
- SHA-256: `fea4ac9f69b8ce75fbfcf5b5d6403253e717dc27c8e70bde36a319e09385634f`.
