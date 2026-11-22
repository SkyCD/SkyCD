# CD Catalog Expert 9.x container layout (`.zdc`)

All integers are little-endian. `Int32` fields are signed; `UInt32` and
`UInt64` fields are unsigned. Container filenames are NUL-terminated ASCII in
fixed-width fields. The supported profile uses `chunkSize = 4096`; the
physical position of chunk `n` is `280 + (n * chunkSize)`. Chunk numbers must
be positive, within the file, and may be visited only once per chain.

## Container header

364 bytes at file offset 0.

| Offset | Size | Type | Meaning |
|---:|---|---|---|
| `0x000` | 8 | bytes | ASCII `AAMVHFSS` |
| `0x008` | 272 | bytes | reserved/profile data; preserved when reading |
| `0x118` | 8 | bytes | ASCII `AASFSSGN` |
| `0x120` | 4 | UInt32 | container checksum/profile value (`csc`) |
| `0x124` | 4 | UInt32 | container checksum/profile value (`oof`) |
| `0x128` | 4 | UInt32 | chunk size; require `4096` |
| `0x12C` | 20 | 5 × UInt32 | profile values `a`–`e`; preserved |
| `0x140` | 4 | UInt32 | first directory-tree chunk |
| `0x144` | 4 | UInt32 | total number of member entries |
| `0x148` | 4 | UInt32 | declared chunk count (must equal the physical count) |
| `0x14C` | 32 | bytes | container key/reserved data; all zero for unencrypted ZDC files |

Reader validation: both magics, a 364-byte header,
`(streamLength - 280) % chunkSize == 0`, declared chunk count equal to
`(streamLength - 280) / chunkSize`, and bounded entry counts. A non-zero
container key is rejected as encryption, not parsed as data.

## Directory-tree chunks

Start at the header's first directory-tree chunk and follow `nextChunk` until
all declared entries have been read. The first 32 bytes are:

| Offset | Size | Type | Meaning |
|---:|---|---|---|
| `0x00` | 4 | Int32 | next directory-tree chunk; `<= 0` terminates |
| `0x04` | 4 | UInt32 | XOR checksum of bytes `0x20..0xFFF` |
| `0x08` | 24 | 6 × UInt32 | reserved/profile values; preserved |

Bytes after `0x20` hold consecutive 512-byte member records (at most seven
per 4096-byte chunk); unused bytes must be zero. Premature termination,
cycles, duplicate chunks, and non-zero unused bytes are rejected.

XOR checksum: split the covered bytes into little-endian UInt32 words and XOR
every word starting from zero.

## Member record

Exactly 512 bytes:

| Offset | Size | Type | Meaning |
|---:|---|---|---|
| `0x000` | 4 | Int32 | first file-index chunk; `-1` means empty member |
| `0x004` | 8 | UInt64 | stored member length (AACS wrapper included) |
| `0x00C` | 8 | UInt64 | timestamp A (SFS 100-nanosecond-like units; raw) |
| `0x014` | 8 | UInt64 | timestamp B; raw |
| `0x01C` | 8 | UInt64 | timestamp C; raw |
| `0x024` | 4 | UInt32 | file type/flags |
| `0x028` | 4 | Int32 | parent entry; `-1` for root members in ZDC |
| `0x02C` | 4 | UInt32 | reserved; require zero |
| `0x030` | 32 | bytes | per-file key; zero for unencrypted ZDC members |
| `0x050` | 140 | bytes | reserved; preserved |
| `0x0DC` | 4 | UInt32 | entry type/flags |
| `0x0E0` | 288 | bytes | NUL-terminated ASCII member filename |

Required ZDC members are `zdc_index.xml`, `{GUID}.xml` disc trees, and
optional `{GUID}.jpg` covers. Path separators, `..`, absolute paths,
case-insensitive duplicates, and names without a NUL terminator are rejected.

## File-index chunks

Follow the member's first file-index chunk until `nextChunk == -1`.

| Offset | Size | Type | Meaning |
|---:|---|---|---|
| `0x00` | 4 | Int32 | next file-index chunk |
| `0x04` | 28 | 7 × UInt32 | reserved/profile values; preserved |
| `0x20` | 4064 | Int32 array | positive values are file-data chunk numbers; zero/negative are unused |

Data is concatenated from referenced file-data chunks in array order and
across file-index chunks in chain order. Cycles, duplicate references,
overlaps with directory/index chunks, and out-of-range references are
rejected. Shared file-data chunks across members are rejected.

## File-data chunks

| Offset | Size | Type | Meaning |
|---:|---|---|---|
| `0x00` | 4 | Int32 | reserved/link value |
| `0x04` | 4 | UInt32 | XOR checksum of the 4064-byte data area |
| `0x08` | 4 | UInt32 | flags; unencrypted ZDC chunks use `0x00000006` |
| `0x0C` | 20 | bytes | reserved; preserved |
| `0x20` | 4064 | bytes | member data |

Flag `0x100` marks encryption (outside the initial scope): a dedicated
unsupported-encryption error is returned instead of corrupt data. Any other
unexpected flag value is rejected as corruption.

## AACS payload wrapper

XML and JPEG member bytes are stored inside an AACS wrapper:

| Offset | Size | Type | Meaning |
|---:|---|---|---|
| `0x00` | 4 | bytes | ASCII `AACS` |
| `0x04` | 4 | UInt32 | profile value; preserved |
| `0x08` | 4 | UInt32 | zero |
| `0x0C` | 4 | UInt32 | one |
| `0x10` | 4 | UInt32 | `0x40000000` |
| `0x14` | 4 | UInt32 | compression method/level; ZDC uses `5` |
| `0x18` | 104 | bytes | zero padding through offset `0x7F` |
| `0x80` | 4 | UInt32 | compressed byte count |
| `0x84` | 4 | UInt32 | uncompressed byte count |
| `0x88` | 4 | UInt32 | CRC-16 of uncompressed bytes |
| `0x8C` | 4 | UInt32 | total wrapper size: `0x90 + compressedByteCount` |
| `0x90` | var | bytes | RFC 1950 zlib stream |

The member record's stored length trims concatenated chunk data before
parsing. Required: total-wrapper-size and compressed-length agreement with
the stored length, exact zlib stream completion, exact inflated length, valid
CRC, and zero padding after the stored member bytes.

CRC-16: reflected CRC-16/IBM (polynomial `0xA001`), initial value `0`,
per byte `crc = (crc >> 8) ^ table[(byte ^ crc) & 0xFF]`, no final XOR.
Check vector: `"123456789"` → `0xBB3D`.

## Writer profile

The writer (`ZdcWriter`) builds a new container deterministically: 4096-byte
chunks, chunks `0..3` reserved (zero-filled past the header), directory tree
from chunk 4, one file-index chain per member, data chunks after all index
chunks. Members sort as `zdc_index.xml`, disc XML by name, covers by name,
then anything else by name; a missing index is synthesized empty. Names encode
as ASCII (max 287 bytes). Payloads wrap in AACS via BCL zlib with field `5`.
Wrapped payloads split into 4064-byte data areas, zero-padded, with XOR
checksums over the padded areas. Counts, chains, checksums, and envelopes are
recomputed; reserved blobs and raw member fields round-trip verbatim, so an
unchanged catalog rewrites byte-identically.
