# Broken X Database binary layout (archive version `0x40`)

All integers are little-endian. Every read is bounded by the remaining input length plus the
safety limits in `BxdFormat`; anything beyond the remaining bytes is reported as truncation,
never read past the buffer.

## File header

| Offset | Size | Meaning |
|---:|---|---|
| `0x00` | 21 | Exact bytes `Broken X Database\r\n\x1A\x00` |
| `0x15` | 1 | Archive version; only `0x40` is supported |
| `0x16` | variable | Root property block |
| after root properties | 4 | `uint32` item count |
| after item count | variable | Exactly `itemCount` preorder item records; no trailing data allowed |

## Length-prefixed string (`BxdString`)

Raw UTF-8 bytes without a terminator:

- Length `< 255`: one-byte length followed by that many bytes.
- Length `255..65534`: byte `FF`, then `uint16` length, then bytes.
- Length `>= 65535`: byte `FF`, `uint16 FFFF`, then `uint32` length, then bytes.

Mapped textual fields (currently: item names) must be valid UTF-8; anything else is
corrupt. The original byte sequence is retained alongside the decoded name so unchanged
catalogs rewrite byte-for-byte.

## Property block

```text
uint8 propertyCount
Property properties[propertyCount]
```

Each property begins with a one-byte descriptor. Bits 7..6 select the payload encoding and
bits 5..0 are the property ID:

| Descriptor mask | Payload |
|---:|---|
| `0x40` | `uint32` |
| `0x80` | `BxdString` |
| `0xC0` | `uint32 byteLength`, then `byte[byteLength]` |
| `0x00` | Invalid on disk; reject |

The complete descriptor byte is the stable property key. Unknown properties of every kind
are preserved verbatim, including their order. Known root properties in a newly created
database are descriptors `0x42`, `0x4E` and `0x4F` (all `uint32`); a canonical empty
v`0x40` database uses values `1`, `1` and `0x04150953`, followed by item count `0`:

```text
0000  42 72 6F 6B 65 6E 20 58 20 44 61 74 61 62 61 73
0010  65 0D 0A 1A 00 40 03 42 01 00 00 00 4E 01 00 00
0020  00 4F 53 09 15 04 00 00 00 00
```

The semantic meaning of individual property IDs has not been established from genuine
files, so no property value is mapped into the domain model; everything round-trips
opaquely and unknown descriptors are listed in the import summary.

## Item record

Every item has the same serialized shape:

```text
uint16    flags
BxdString name
uint32    field14
uint32    field18
PropertyBlock metadata
```

`field14`, `field18` and unknown base flag bits are preserved on round trip even though
SkyCD has no mapped field for them. The name plus the fixed fields and the complete
metadata block belong to the item; there are no global tables or offsets.

The high flag bits encode the tree in preorder:

- `0x2000`: this item is the last sibling at its depth.
- `0x4000`: this item has no children.
- `0x6000` is structural only. These bits are removed before the base flags are preserved
  and restored from the exported tree when writing.

Base flag bit `0x0001` is the reserved protected-record (encryption) marker: records
carrying it are reported with a dedicated error and never imported or rewritten. Genuine-file
verification of this marker is still open; detection fails loudly rather than silently
importing protected data.

## Reader algorithm

```text
read root property block
remaining = read uint32 itemCount
stack = [database root]

repeat remaining times:
    read one item record under stack.top
    attach it after the previous sibling

    if (flags & 0x4000) == 0:
        stack.push(item)                 // next record is first child
    else:
        while (flags & 0x2000) != 0:
            stack.pop()                  // sibling chain ended
            use the completed parent's flags for the next unwind decision
```

Invariants: preorder, exactly `itemCount` records, depth at most 64, no inferred records.
A record stream that ends with folders still open, or with bytes left over, is corrupt.

## Writer algorithm

1. Traverse selected records in stable preorder.
2. Start with each item's preserved base flags (`flags & ~0x6000`).
3. Add `0x4000` when it has no exported children.
4. Add `0x2000` when it is the last exported sibling.
5. Write name, the two fixed fields, then the property count and property entries in the
   preserved order.
6. Backfill/write the exact preorder record count as `uint32` before the first item.

## Safety limits

`MaxItemCount` (1M), `MaxDepth` (64), `MaxStringBytes` (8 MiB), `MaxBlobBytes` (64 MiB),
total database size (256 MiB). Counts and lengths that exceed the remaining input or a
limit are corrupt; nesting beyond the limit is corrupt; there is no recursion (explicit
stacks on both paths) and no inferred records.
