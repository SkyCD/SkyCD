# Broken X Database import/export

The `brokenx-bxd` plugin reads and writes Broken X Disk Manager databases
(`.bxd`, archive version `0x40`) natively: open a file with **File -> Open** to
import it as a catalog, and save to write one back. Files with any other
archive version are rejected with an unsupported-version error.

Only files starting with the exact 21-byte signature and version byte are
recognized; the `.bxd` extension alone never claims a file.

## Round trip

Unchanged catalogs rewrite byte-for-byte: item order and hierarchy, names (kept
with their original byte sequences), the two fixed 32-bit fields, unknown base
flag bits and every property — including unknown descriptors and their order —
are preserved. Structural flag bits are recomputed from the exported tree.

Domain mapping is structural: the database becomes the media root, items with
children become folders, leaf items become files. Empty folders keep their
preserved flags so an unchanged file still rewrites identically, but a leaf
without kind metadata presents as a file after an edited round trip — the
format itself does not distinguish them at the structural level.

Property descriptor semantics (descriptions, categories, sizes, timestamps)
have not been established from genuine files, so values round-trip opaquely and
unknown descriptors are listed in the import summary instead of being dropped.

## Merge import and subtree export

Merge several `.bxd` sources with `BxdMergeService`: rows are keyed by node ID,
the first source wins every conflict with a warning, and missing intermediate
folders are synthesized so the result stays consistent.

Export selected disks or folders (plus descendants) with
`BxdSubtreeSelector`, then write the result: the writer stores the exact
preorder record count and rebuilds structural bits from the exported tree.
Unknown or empty selections are rejected with an actionable error.

## Protected records and limits

Records carrying the reserved protected bit are reported with a dedicated
password-protection error naming the record; decryption is not implemented and
protected data is never silently omitted or rewritten. Genuine-file
verification of the marker bit is still open (see `BINARY-LAYOUT.md`).

Safety limits bound items (1M), nesting depth (64), string/blob payloads and
the total database size; anything beyond the remaining input or a limit is
reported as corruption. Written files use the canonical empty root properties
when the source rows carry none.
