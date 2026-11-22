# Playlist import and export

`SkyCD.Plugin.FileFormat.Playlist` reads and writes catalog paths as playlists through three
file-format capabilities that share one enable toggle in **Options → Plugins**:

| Format id | Extensions | MIME types |
| --- | --- | --- |
| `skycd-m3u` | `.m3u`, `.m3u8` | `audio/x-mpegurl`, `application/x-mpegURL` |
| `skycd-pls` | `.pls` | `audio/x-scpls`, `audio/scpls` |
| `skycd-xspf` | `.xspf` | `application/xspf+xml` |

All three are read/write. `GetOpenFormats()` and `GetSaveFormats()` expose each format id with its
extensions, so **File → Open** and **File → Save As** keep the same choices as before the
consolidation. Each format keeps its own payload document shape because `WriteAsync` casts the host
payload to that format's document type.

## M3U (`skycd-m3u`)

`M3uPlaylistDocument` carries `M3uPlaylistEntry(Path, Title?, DurationSeconds?)` items. Reading
skips comments and blank lines, pairs `#EXTINF:<seconds>,<title>` with the following path line, and
leaves title and duration unset when no `#EXTINF` precedes an entry. Writing emits `#EXTM3U`, a blank
line, then for each entry the optional `#EXTINF` line followed by the path and a blank line. An entry
without a title and duration is written as its path only.

## PLS (`skycd-pls`)

`PlsPlaylistDocument` carries `PlsPlaylistEntry(Path, Title?, SizeBytes)` items. The file must start
with `[playlist]`; other `key=value` lines are ignored. Entries are read from `File<n>`, `Title<n>`
and `Length<n>` keys, missing sizes default to zero, and the resulting list is sorted by path with
ordinal comparison. Writing emits `[playlist]`, one `File`/`Title`/`Length` block per entry,
`NumberOfEntries`, and `Version=2`.

## XSPF (`skycd-xspf`)

`XspfPlaylistDocument` carries `XspfPlaylistEntry(Path, Title?, Creator?, DurationMilliseconds?)`
items; `Location` is an alias of `Path`. The root must be
`<playlist xmlns="http://xspf.org/ns/0/">`. Each `<track>` with a non-empty `<location>` becomes an
entry; absolute `file:` URIs are decoded to filesystem paths, including Windows drive letters.
Writing emits a version 1 playlist with `location`, optional `title`, `creator`, and `duration`
elements.

## Output compatibility

The consolidation moved the capabilities into one assembly without changing their serializers.
For every existing fixture, reading produces the same entries and writing produces the same bytes as
the three former plugins (`SkyCD.Plugin.FileFormat.M3u`, `.Pls`, `.Xspf`), including text encoding
(UTF-8 without BOM) and platform line endings for the text formats. XSPF output remains the compact
document form produced by `XDocument.ToString(SaveOptions.None)`.

## Plugin identity and state

The merged plugin keeps an explicit id, `SkyCD.Plugin.FileFormat.Playlist`, so plugin discovery
creates one plugin entry and one enable toggle. The previous per-format ids are retired. Existing
`PluginDocument` rows for them are preserved but marked unavailable by the next discovery pass, the
same behavior used for other consolidated plugins. The merged plugin is discovered with its default
enabled state; enabled/disabled choices recorded against the retired ids do not carry over.

## Tests

`tests/SkyCD.Plugin.FileFormat.Playlist.Tests` covers single-plugin discovery, open/save format
registration, read/write round trips, and exact output for the `.m3u8`, `.pls` and `.xspf` fixtures.
