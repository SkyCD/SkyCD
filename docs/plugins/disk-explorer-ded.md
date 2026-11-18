# Disk Explorer Professional database import/export

The `disk-explorer-ded` plugin reads and writes Disk Explorer Professional
database files (`.ded`). It is a file-format capability: **File -> Open**
imports a `.ded` database as a catalog, and saving writes one back. The plugin
also exposes volume transfer helpers so selected volumes can be imported into
or exported from an existing database.

A file is only claimed when its name ends in `.ded` **and** the content passes
a bounded header check (magic, version, table bounds). The extension alone
never claims a file.

## Container layout

All integers are little-endian. Offsets in this document are hexadecimal.

### Header (0x50 bytes)

| Offset | Size | Field |
| ---: | ---: | --- |
| 0x00 | 4 | Magic `DEDB` |
| 0x04 | 4 | Format version, currently `0x00000102` |
| 0x08 | 4 | Offset-table slot count |
| 0x0C | 4 | Populated record count (distinct non-zero slots) |
| 0x10 | 4 | Header flags |
| 0x14 | 8 | Database creation time (FILETIME, 100 ns ticks since 1601-01-01 UTC) |
| 0x1C | 4 | Reserved flags (preserved, not interpreted) |
| 0x20 | 48 | Database title, NUL-padded strict Windows-1252 |

Only bit `0x1` of the header flags is interpreted: it marks the database as
password protected. Other flag bits are preserved.

### Offset table

`slotCount` 32-bit file offsets follow the header immediately. Each non-zero
slot points at one record extent; `0` means an unused (sparse) slot. Slots are
sparse and unsorted, and several slots may reference the same offset, in which
case the record is parsed once. New files start with 1024 slots and grow when
the database needs more.

### Record layout

Every record begins with a 12-byte prefix so extents can be read without
parsing:

| Offset | Size | Field |
| ---: | ---: | --- |
| 0x00 | 4 | Object kind code |
| 0x04 | 4 | Object flags (preserved per object) |
| 0x08 | 4 | Record length (includes the prefix, excludes page padding) |

Known kinds: `1` database, `2` category folder, `3` volume, `4` folder,
`5` archive, `6` file. A known record continues after the prefix with:

- object id (`uint64`) and parent id (`uint64`, `0` for roots);
- name, path, and description text triplets;
- logical size and aggregate size (`uint64`);
- created, modified, and added timestamps (`uint64` FILETIME, preserved as raw
  values, including sentinel values such as `ulong.MaxValue`);
- file count and folder count (`uint32`);
- child id list and category-membership id list (counted `uint64` arrays);
- extension field list;
- opaque payload list;
- tail blob length (`uint32`) and bytes.

A text triplet is encoding id (`uint32`), byte length (`uint32`), then bytes.
Encoding `0` is strict Windows-1252 (the default) and `1` is strict UTF-8; any
other value is corruption. Strings that are not representable in Windows-1252
are written as UTF-8, so non-ASCII names round-trip without loss.

Extension fields and payloads share one wire shape: a `uint32` count followed
by entries of `uint32` tag/kind, `uint32` byte length, then bytes. Extension
tags are opaque. Payload kinds are `1` stored content, `2` thumbnail, and
`3` generated description; any other kind is preserved as an unknown payload.

Records whose kind code is not 1..6 are never parsed: the raw record bytes are
kept and re-emitted verbatim, preserving their ids at the documented prefix
positions.

## Import (read)

Catalog rows are produced for the full object graph: database and folder rows
map to folders, volumes to media, and archives/files to file rows. Rows carry
the name, parent, kind, size (`aggregateSize` when non-zero, else
`logicalSize`, as invariant text), display dates, child count, and typed
metadata properties prefixed `ded:` (ids, raw kinds/flags, paths, FILETIMEs,
category ids, extension and payload blobs, tail and raw-record blobs, per-field
encodings, header metadata). The report document also keeps the parsed
`DedDatabase` so a save writes back the exact model.

Category membership is metadata only; membership subtrees are not duplicated
into the tree rows.

## Export (write)

Write accepts a `DedDatabase`, a `DedReportDocument`, a `DedExportRequest`, or
plain catalog rows. Rows without explicit `ded:` ids are assigned deterministic
ids above the highest existing id, and hierarchy is linked only when the
declared parent matches, so generic rows never need random identifiers.

Selected-volume import/export is provided by `DedVolumeTransfer`:

- `SelectVolumes` accepts volume ids only, copies the selected subtree,
  re-roots it at the selected volumes, and prunes children that were not
  selected;
- `Copy` transfers the whole database;
- `Merge` appends a selection into an existing database, remapping ids;
- `DedTransferOptions` controls three omission switches independently:
  stored contents, thumbnails, and generated descriptions. Unknown payloads
  are always retained.

Export never mutates the source database; all transfer operations copy objects
and payload arrays first.

## Determinism and atomic save

Records are written in database order at sequential offsets calculated from
the header and table size; repeated saves of the same model produce identical
bytes. File saves write a temporary sibling and atomically replace the
destination, leaving no temporary file behind. A failed save never truncates
the previous database.

## Validation and diagnostics

Malformed input fails with typed exceptions and bounded diagnostics:

- `DedCorruptException` — bad magic, truncated/overlapping records, offsets
  outside the file, table bounds, populated-count mismatches, unknown
  encodings, missing parents/children, duplicate ids, or object cycles;
- `DedUnsupportedVersionException` — any version other than `0x00000102`;
- `DedLimitExceededException` — table slots, object count, record size, string
  size, payload size, or buffered file size beyond the read limits;
- `DedTextEncodingException` — a text field that is not valid in its declared
  encoding;
- `DedModelException` — an invalid model on write, such as a dangling child or
  a parent/child mismatch;
- `DedTransferException` — an invalid volume selection or transfer request.

Password-protected databases are never treated as corrupt: reading without a
password raises `DedPasswordRequiredException` and reading with any password
raises `DedPasswordInvalidException`, because the password algorithm is not
implemented. Passwords are never logged or retained.

Cycles are detected without recursion (iteratively), so deep hierarchies do
not overflow the stack. Reads restore the stream position and buffer
non-seekable streams before parsing.
