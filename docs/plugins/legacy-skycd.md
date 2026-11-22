# Legacy SkyCD Catalog Formats (`legacy-skycd`)

Read/write plugin `SkyCD.Plugin.FileFormat.LegacySkyCD` for the catalogs written by the
original VB6 SkyCD application. One assembly provides all four legacy format ids, so a single
entry in Options → Plugins controls every legacy SkyCD catalog:

| Format id | Extension | Display name | Read | Write |
| --- | --- | --- | --- | --- |
| `legacy-scd` | `.scd` | SkyCD Legacy Text Format | Yes | Yes |
| `legacy-scd-binary` | `.scd` | SkyCD Legacy Binary Format | No (the text reader imports both) | Yes |
| `legacy-cscd` | `.cscd` | SkyCD Compressed Text Format | Yes | Yes |
| `legacy-ascd` | `.ascd` | SkyCD Advanced Format | Yes | Yes |

The plugin is discovered through assembly metadata and capability types; no `plugin.json` is used.
The four capability classes are distinct selectable formats, not a mode switch, because each one is
a separate entry in Open/Save As.

## Text format (`.scd`)

Line oriented UTF-8 text. Blank lines and lines whose first non-space character is `#` are ignored.
Every other line is one catalog entry:

```
<path>
<path-with-[size]>
```

A size token sits at the start of the final `\`-separated segment and wins over any bracketed text
in earlier segments or in the file name, so `[Disk]\Folder [2023]\[1.5        MB   ] File [Remix].txt`
is a file named `File [Remix].txt` in `[Disk]\Folder [2023]` with 1572864 bytes. A line without a
valid size token in its final segment is a directory.

Accepted size units are `Bits`, `Bytes`/`B`, `KB`, `MB`, `GB` and `TB`, case-insensitive, with `.`
or `,` as the decimal separator. Sizes are written back with the original ten-character numeric field
and unit padding (`[1.171875   KB   ]`), which is why files edited through SkyCD may differ in
spacing from the original catalog while keeping identical entry data.

Reading `.scd` first checks for the `# SkyCD File` signature and delegates binary catalogs to the
VB6 binary reader, so both variants open through `legacy-scd`. Writing offers the text and binary
layouts as separate Save As choices.

## VB6 binary format (`.scd`, `legacy-scd-binary`)

The binary layout is the VB6 `skyFileProperties` structure followed by a flat node table: each node
stores its parent key, key and display text as Windows-1252, `UInt16`-length-prefixed strings. The
writer reproduces the historical layout byte for byte (fixed 127-byte header, 5392-byte properties
block, `OLE Automation` date and node count) and numbers nodes `n0000001` upwards in path order.
Characters outside Windows-1252 are written as `?`; the binary layout stores only the legacy tree,
so modern-only metadata is dropped.

The reader validates the node count, rejects truncated headers, empty or duplicate keys, missing or
cyclic parents and embedded nulls with typed failures that name the reason (`Invalid legacy binary
SCD catalog: ...`).

## Compressed text format (`.cscd`)

Deflate-compressed text using exactly the `.scd` line grammar and size parsing, plus the same
`[size] ` prefix in the final segment. Compression is applied around the text stream only; no header
is added. Reading and writing share the text format's parser and formatter, so a `.scd` and a
`.cscd` holding the same entries import identically.

## Advanced format (`.ascd`)

Deflate-compressed SQL-like text produced by later SkyCD builds. The first non-empty line must be
`# format: skycd-nf <version>`; entries follow as either
`INSERT INTO list (...) VALUES ('id', 'name', 'parent', 'type', 'properties', 'size', 'application-id')`
records or as JSON document lines. The parser is data-only: no SQL is executed and only a single
`VALUES (...)` clause per line is accepted. Legacy type names (`scdFolder`, `scdCDDisk`,
`scdNetworkRecource`, ...) are normalized to `Folder`, `Media`, `NetworkFolder`, `NetworkResource`
and `File`, and restored on write through the `legacyType` field. `<?Application_ID?>` is stored as
the empty GUID.

Writing emits the `skycd-nf` header followed by one JSON document per entry, which is the
round-trip form the plugin produces today.

## Plugin identity and migration

The merged plugin keeps the id `SkyCD.Plugin.Legacy.Scd` so persisted plugin documents, enabled
state and configuration keys stay valid for the `.scd` reader, which already hosted both `.scd`
capabilities. The retired ids `SkyCD.Plugin.Legacy.Cscd` and `SkyCD.Plugin.Legacy.Ascd` remain in
existing catalogs as unavailable plugin documents and no longer appear in Options → Plugins; their
formats are now controlled by the merged toggle.

Adding another legacy SkyCD variant means adding a capability class to this assembly rather than
creating another `Legacy.*` project.

## Tests

`tests/SkyCD.Plugin.FileFormat.LegacySkyCD.Tests` covers the owned text/binary `.scd` fixtures, the
historical `gamez.scd` sample, malformed binary trees, size parsing and padding, `.cscd` round-trips,
`.ascd` header validation, legacy type normalization, and that Save As still exposes all four format
ids.
