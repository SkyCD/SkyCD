# Advanced Disk Catalog database decoder (version 1.x)

This document is the implementation oracle for `SkyCD.Plugin.Adc`. All integers are
little-endian. Strings are length-prefixed UTF-16LE (no terminator, no byte-order mark).
Every read is bounded by the enclosing record frame and by the limits in `AdcFormat`;
anything beyond the available bytes is reported as truncation and never read past the buffer.

## Signature and content detection

ElcomSoft Advanced Disk Catalog files carry the PRONOM fmt/1370 signature
`0B000000 4144432E436174616C6F67` at the beginning of the file: a `uint32` length of 11
followed by the ASCII text `ADC.Catalog`.

Detection combines the extension with content:

1. When a file name is available, its extension must be `.cat` or `.adc`.
2. The header length, signature, major version and offset fields must be valid.
3. A file that begins with a DER-encoded ASN.1 SEQUENCE using long-form length
   (`0x30`, then a length byte with bit 7 set) is a Windows Authenticode security catalog.
   It is rejected with `AdcAuthenticodeCatalogException` before any format parsing and is
   never claimed by `CanHandleContent`.

Only major version `1` is supported; the minor version is accepted as-is and reported.

## File header (55 bytes)

| Offset | Size | Meaning |
|---:|---:|---|
| `0x00` | 4 | `uint32` header length; must be `11` |
| `0x04` | 11 | ASCII `ADC.Catalog` |
| `0x0F` | 2 | `uint16` format major version (must be `1`) |
| `0x11` | 2 | `uint16` format minor version |
| `0x13` | 4 | `uint32` catalog flags (preserved, not interpreted) |
| `0x17` | 4 | `uint32` framed record count |
| `0x1B` | 8 | `uint64` body offset in bytes; must be `>= 55` and `<= file length` |
| `0x23` | 8 | `uint64` comment-table offset; must be `>= body offset` and `<= file length` |
| `0x2B` | 4 | `uint32` comment count |
| `0x2F` | 4 | `uint32` category record count |
| `0x33` | 4 | `uint32` reserved (ignored) |

The body occupies `[bodyOffset, commentTableOffset)` and contains exactly `recordCount`
framed records with no padding. The comment table occupies `[commentTableOffset, EOF)`.

## Record framing

Every record starts with:

| Size | Meaning |
|---:|---|
| 4 | `uint32` record type |
| 4 | `uint32` payload length in bytes |

The payload immediately follows and is parsed strictly within its declared length. A record
whose payload is not consumed exactly is corrupt. Unknown record types are skipped using the
declared length, recorded in the import summary, and never guessed at. Known types:

- `0x0001` volume
- `0x0002` item
- `0x0003` category

## Primitive encodings

- **String**: `uint32` count of UTF-16 code units, then `count * 2` bytes of UTF-16LE.
  Invalid surrogate sequences are corrupt. The empty string is representable except for
  item and category names, which must be non-empty.
- **Preserved property block** (volume and item records):

  ```text
  uint16 propertyCount
  Property properties[propertyCount]
  ```

  Each property starts with one descriptor byte. Bits 7..6 select the payload encoding and
  bits 5..0 are the property identifier (0..63):

  | Descriptor bits | Payload |
  |---|---|
  | `00` | `uint32` |
  | `01` | String |
  | `10` | `uint32 byteLength`, then `byteLength` bytes |
  | `11` | Reserved; corrupt |

  Optional properties with no typed SkyCD field are preserved in the node row's
  `AdcProp_XX` extension bag, together with their on-disk order.

## Volume record (type `0x0001`)

```text
uint32  volumeId
uint8   volumeType       (0 unknown, 1 fixed, 2 removable, 3 network,
                          4 CD-ROM, 5 DVD, 6 RAM disk)
uint8   flags
uint16  reserved
uint32  serialNumber
uint64  capacityBytes
String  label
String  source           (drive letter, device path or empty)
uint16  propertyCount
Property properties[...]
```

Volume identifiers are unique across the catalog.

## Item record (type `0x0002`)

```text
uint32  itemId
uint32  parentId         (0 = direct child of the volume, else itemId)
uint32  volumeId
uint8   kind             (0 file, 1 folder)
uint8   attributes       (DOS attribute bit mask)
uint16  reserved
uint32  typeCode         (stored file-type identifier)
uint64  sizeBytes
uint64  createdFileTime  (Windows FILETIME, 0 = absent)
uint64  modifiedFileTime
uint64  accessedFileTime
uint32  commentIndex     (1-based into the comment table, 0 = none)
uint16  categoryCount
uint32  categoryIds[categoryCount]
String  name
uint16  propertyCount
Property properties[...]
```

Item identifiers are unique across the whole catalog. `sizeBytes` is 64-bit, so files larger
than 4 GiB are represented exactly. A creation, modification or access time that cannot be
converted from a Windows FILETIME is corrupt.

## Category record (type `0x0003`)

```text
uint32  categoryId
String  name
```

Category identifiers are unique. The number of category records must equal the header's
category count.

## Comment table

The comment table starts at `commentTableOffset`:

```text
uint32  entryCount                 (must equal the header comment count)
Entry   entries[entryCount]
byte    data[...]
```

Each entry is `uint32 offset`, `uint32 byteLength`. Offsets are relative to the start of the
comment table and must not point into the entry array. Both offset and byte length must be
even, and the referenced range must stay inside the file. Comments are decoded as UTF-16LE.
Item `commentIndex` is one-based; index `0` means "no comment".

## Parent-reference and hierarchy rules

- `parentId == 0` attaches the item to its `volumeId` root.
- Otherwise `parentId` must name an existing item with the same `volumeId`.
- The referenced parent must be a folder (`kind == 1`).
- Parent references must form a tree: a cycle is rejected with `AdcCycleException`, and an
  item chain longer than 256 records is rejected as exceeding the nesting limit.
- Duplicate volume, item or category identifiers, unknown volumes, unknown categories,
  out-of-range comment indexes and unknown parents are all hard errors. A partially linked
  catalog is never returned.

Empty folders are first-class records, so a folder with no children is preserved. File and
folder names may repeat under different parents; node identifiers use the unique item
identifier rather than the path.

## Safety limits

| Limit | Value |
|---|---:|
| File size | 256 MiB |
| Framed records | 1,000,000 |
| String length | 1,048,576 UTF-16 code units |
| Property blob | 64 MiB |
| Properties per record | 256 |
| Categories per item | 4,096 |
| Item nesting depth | 256 |

Cancellation is observed while copying the stream and every 500 records. Limits are checked
before allocation.

## Fixture contract

`tests/SkyCD.Plugin.Adc.Tests/Fixtures` contains redistributable synthetic catalogs built by
the test fixture writer (the plugin itself is read-only), a `manifest.json` that lists every
expected volume and entry (path, kind, size, timestamps, comments and categories), malformed
variants, and a Windows Authenticode security catalog negative fixture. Tests read the
committed bytes through the plugin and compare the resulting rows with the manifest; no
Advanced Disk Catalog installation or external executable is required.
