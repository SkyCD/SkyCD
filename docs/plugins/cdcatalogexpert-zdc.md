# CD Catalog Expert 9.x import/export

The `cd-catalog-expert-zdc` plugin reads and writes CD Catalog Expert 9.x
catalogs (`.zdc`, `chunkSize = 4096`) natively: open a file with **File ->
Open** to import it as a catalog, and save to write one back. Chunk sizes
other than 4096 are rejected as unsupported.

Only streams carrying both container magics, the ZDC chunk size, and a
chunk-aligned length are recognized; the `.zdc` extension alone never claims
a file, and a non-`.zdc` extension is never claimed.

## Catalog structure

The import reconstructs the catalog tree the profile stores:

- **Catalog** — the synthetic root folder (`ZdcNodeKind = catalog`) carrying
  the catalog name/description, container profile values, and reserved blob.
- **Groups** — nested folders from the index (`ZdcNodeKind = group`).
- **Discs** — one media row per `L` entry (`ZdcNodeKind = disc`) with every
  disc attribute preserved as its original invariant text: source path,
  created/modified stamps, description, volume label, file system, media
  type, volume serial, capacity/free space, options, scan flags `SO_A..SO_G`,
  keywords, storage location, index code, barcode, cover member, and rating.
- **Disc contents** — directories and files from the disc-tree member
  (`ZdcNodeKind = directory` / `file`) with attributes, timestamps,
  descriptions, keywords, and the size of each file.

Unknown XML attributes are retained as JSON extension arrays and written back
verbatim. Covers are embedded as Base64 in the disc row and restored to the
cover member on save. Disc GUID references stay visible as `ZdcDiscGuid`.

## Round trip

Unchanged catalogs rewrite byte-for-byte: header profile values and reserved
blobs, member timestamps/flags/reserved fields, per-chunk reserved words, and
payload bytes (including their AACS profile) are preserved, while chains,
counts, checksums, and AACS envelopes are recomputed. Repeated saves of the
same rows produce identical containers.

Rows that do not come from a ZDC import are also accepted: files and folders
outside a media row are rolled up into a synthesized disc, and missing disc
GUIDs are derived deterministically from the catalog and disc identity, so
importing generic rows never needs random identifiers.

## Diagnostics

Missing covers are reported as warnings and the disc is kept. Unknown
elements are reported and their contents skipped. A missing disc-tree member,
duplicate tree reference, invalid number/flag/GUID, or a non-Windows-1252
byte fails the import with a message naming the member and field. Non-zero
file/entry type flags are reported in the import summary instead of being
dropped.

## Encoding and safety limits

All XML members are decoded as strict Windows-1252; characters that cannot be
represented are rejected on both read and write. DTDs and external entities
are prohibited. Bounds cap nesting depth (64), element count (2,000,000),
attributes per element (64), text/attribute length (4 MiB), document
characters (256M), the container (256 MiB), stored member size (64 MiB),
inflated payload size (128 MiB), and member count (1M). Cancellation is
honored while parsing.

## Encrypted containers and members

A non-zero container key, a per-file key, or the `0x100` file-data flag aborts
the import with a dedicated encryption error naming the member; decryption is
not implemented and encrypted data is never silently omitted or rewritten.
