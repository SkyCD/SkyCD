`binary-sample.scd` is an owned synthetic fixture, with four nodes: a disk,
a folder, a 1536-byte file, and an empty folder. `text-sample.scd` describes
the same tree in the legacy text format.

Binary layout is taken from commit `c0567ab`, `modTypes.bas` and
`SimpleCDDatabaseBrowser.ctl` (`FileSave`/`FileOpen`):

- 127-byte fixed ANSI header beginning `# SkyCD File\r\n`.
- 5392-byte `skyFileProperties` record, including a Double node count at
  absolute byte offset 391.
- Node records beginning at offset 5519. Each has Parent, Key, and Text,
  each encoded as a little-endian UInt16 byte length followed by ANSI bytes.
- No array descriptor is stored by VB6 binary-mode `Put` for this array.

The importer uses Windows-1252 for legacy ANSI strings. Writing uses the
existing UTF-8 text SCD format; binary export is not implemented.
