# CD Catalog Expert 9.x import/export

The `cdcatalogexpert-zdc` plugin reads and writes CD Catalog Expert 9.x
catalogs (`.zdc`, `chunkSize = 4096`) natively: open a file with **File ->
Open** to import it as a catalog, and save to write one back. Chunk sizes
other than 4096 are rejected as unsupported.

Only streams carrying both container magics, the ZDC chunk size, and a
chunk-aligned length are recognized; the `.zdc` extension alone never claims
a file.

## Round trip

Unchanged catalogs rewrite byte-for-byte: header profile values and reserved
blobs, member timestamps/flags/reserved fields, per-chunk reserved words, and
payload bytes (including their AACS profile) are preserved, while chains,
counts, checksums, and AACS envelopes are recomputed.

Domain mapping is structural: the container becomes the media root and every
member becomes a file row; disc XML payloads are preserved opaquely (with
SHA-256 in the row) rather than expanded into deeper hierarchies, because the
profile stores no parent/child relations between members (parent entries are
`-1` roots). Non-zero file/entry type flags are reported in the import
summary instead of being dropped.

## Encrypted containers and members

A non-zero container key, a non-zero per-file key, or the `0x100` file-data
flag aborts the import with a dedicated encryption error naming the member;
decryption is not implemented and encrypted data is never silently omitted
or rewritten.

## Limits

Safety bounds cap the container (256 MiB), stored member size (64 MiB),
inflated payload size (128 MiB), and member count (1M); anything beyond the
remaining input or a bound is reported as corruption. Malformed names, broken
chains, shared chunks, bad checksums/CRCs, invalid padding, and
non-well-formed XML or non-JPEG payloads are rejected the same way.
