# Executable metadata and icon plugin

Implements #733 and #924 on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Metadata.Executable/` → plugin id `SkyCD.Plugin.Metadata.Executable`.
Tests: `tests/SkyCD.Plugin.Metadata.Executable.Tests/`.

This plugin claims Windows executables and reads two things from them: the version information Windows shows in
a file's properties, and the program's own icon, which it stores as a PNG during indexing so the catalog can show
it later without the file being present. It also reads the icon sources the other platforms use, because Linux
and macOS do not embed artwork in their binaries: macOS `.icns` containers, the ISO 9660 volume inside a type 1
AppImage, and the path a freedesktop `.desktop` launcher names.

## What it provides

Three independently discovered capabilities in one assembly:

| Capability | Type | Schema |
|---|---|---|
| Executable facts and version strings | `ExecutableMetadataPlugin : IMediaMetadataCapability` | `skycd.executable.metadata` v1 |
| Bounded icon preview read from the bytes | `ExecutableIconPlugin : IMediaPreviewCapability` | `skycd.executable.icon` v2 |
| Launcher icon resolved through the local path | `LauncherIconPlugin : ILocalMediaPreviewCapability` | `skycd.executable.icon` v2 |

Discovery uses assembly metadata and implemented capability types only — there is no `plugin.json`.

The preview descriptors are marked `IndexTimePreview`, so the indexing pass stores the icon in the catalog
together with the metadata. The browser then renders it from the cached artifact, which means the icon survives
the media going offline, and reopening the item never reads the program again. Preview schema version 2 covers
the platform icon sources, so previews cached by version 1 are re-extracted.

The byte-reading icon capability claims `.exe`, `.dll`, `.scr` and `.com` with the common Windows executable
MIME types (`application/vnd.microsoft.portable-executable`, `application/x-msdownload`, `application/x-msdos-program`,
`application/x-dosexec`), plus `.icns` and `.appimage` with `image/icns`, `image/x-icns`,
`application/x-appimage` and `application/x-iso9660-appimage`. The launcher capability claims `.desktop` and
`application/x-desktop`; the metadata capability keeps the Windows extensions only.

**Nothing is extracted to disk and no file is modified.** The headers, the resource directory nodes, the icon
payload and the AppImage volume are read through the host's bounded `IMediaSource` or through the host-owned
local path; only the bytes actually parsed are charged to the source budget.

## Supported files

| Source | Metadata | Icon |
|---|---|---|
| PE32 and PE32+ images (`.exe`, `.dll`, `.scr`) | Header facts, version resource, icon facts | Yes, from `RT_GROUP_ICON` / `RT_ICON` |
| Bare DOS executables (`MZ` without a reachable PE header, e.g. `.com`-style stubs) | `Container = mz-16` only | No |
| A PE image with no resource directory | Header facts | No |
| macOS `.icns` container | No values | Yes, the largest decodable PNG or JPEG chunk |
| AppImage with an ISO 9660 payload (type 1) | No values | Yes, from `.DirIcon` in the appended volume |
| AppImage with a squashfs payload (type 2) | No values | No; no compliant reader, see below |
| A plain ELF or Mach-O executable | No values | No icon resource exists in the bytes |
| `.desktop` launcher | No values | Yes, the `Icon=` path or theme name, while the file is local |
| A damaged header, section table, resource directory or container | Whatever was read before the damage | No |
| Any other file | No values | No |

Recognition is structural: Windows images must start with `MZ` and a PE image must also carry the `PE\0\0`
signature the DOS stub points at; `.icns` must start with the container signature; an AppImage must start with
an ELF header whose AppImage marker says ISO 9660 or squashfs. The extension is not trusted.

## Metadata schema

| Key | Type | Unit | Meaning |
|---|---|---|---|
| `Container` | Text | – | `pe` for a PE image, `mz-16` for a bare DOS executable |
| `Architecture` | Text | – | `x86`, `x64`, `arm64`, `arm`, `ia64`, `thumb`, or `unknown-0x…` |
| `IsExecutableImage` | Boolean | – | True when the image can be started rather than only loaded as a library |
| `IsDotNet` | Boolean | – | True when the image declares a CLR data directory |
| `PeFormat` | Text | – | `PE32` or `PE32+` |
| `FileVersion`, `ProductVersion` | Text | – | The resource's version strings, as written |
| `CompanyName`, `ProductName` | Text | – | `VS_VERSION_INFO` company and product strings |
| `FileDescription`, `OriginalFilename`, `InternalName`, `LegalCopyright` | Text | – | The remaining `StringFileInfo` values the catalog can use |
| `IconImageCount` | Integer | – | Number of images the first icon group declares |
| `LargestIconWidth`, `LargestIconHeight` | Integer | px | Largest declared icon size; `256` when the group writes zero |

Values an image does not carry are omitted, so "unknown" stays distinguishable from "empty". Text values are
trimmed and cut at `MaxMetadataValueCharacters` (512 by default).

Only `StringFileInfo` keys with a field in the schema are published; translation tables and other keys are
ignored. The `VS_VERSION_INFO` reader honors the format's two length styles — the fixed block declares its length
in bytes, string nodes declare theirs in UTF-16 code units.

## Icon preview

The preview follows the icon format's own path:

1. The plugin reads the first usable `RT_GROUP_ICON` directory and sorts its declared images by area, then by bit
   depth. A group whose declared payloads are all damaged is skipped and the next group is tried.
2. Resource ids in the group are one-based indexes into the `RT_ICON` list, which is how Windows stores them.
3. An `RT_ICON` payload is either a `BITMAPINFOHEADER` bitmap (palette for 1/2/4/8 bpp, 16/24/32 bpp direct, XOR
   rows bottom-up, then a 1-bit AND mask) or, since Windows Vista, a bare PNG decoded with SkiaSharp. Both are
   accepted.
4. 32-bit images carry their own alpha; the AND mask is still applied where it marks transparency, so the rounded
   corners of older icons survive.
5. The image is drawn onto a transparent square. An icon that fits keeps its own pixels and is centred, so a
   16×16 icon is never blurred into a 128×128 smear; a larger one is box-filtered down with its longer edge fitted
   to the square, averaging in premultiplied alpha so transparent margins never darken the edge.
6. The result is encoded as an 8-bit RGBA PNG (`image/png`) with SkiaSharp.

The stored PNG is therefore always square and exactly `IconSize` pixels on a side (128 by default, clamped to
16–128).

### macOS `.icns`

An `.icns` file is an eight-byte `icns` header followed by chunks, each with a four-character type and a
big-endian length that includes the eight-byte chunk header. Only chunks whose payload is a standalone PNG or
JPEG are decoded — the modern `ic07`–`ic14` and `icp4`–`icp6` types. Chunks are sorted by their documented edge
and by payload size, and the first one that decodes within the existing byte and pixel bounds wins. At most 256
chunks are walked and at most `MaxIndexedIconImages` payloads are decoded, so a crafted chunk list is truncated
rather than followed.

### AppImage type 1

An AppImage is an ELF runtime followed by the packed application directory; the type byte at offset ten selects
ISO 9660 (type 1) or squashfs (type 2). For type 1 the plugin locates the volume's primary volume descriptor in
the first 4 MiB of the file, then opens the volume over a windowed, bounded stream — the archive is never
mounted, extracted to disk or executed. The icon is the root `.DirIcon` entry, which is usually a Rock Ridge
symlink to a PNG beside it, so links inside the volume are followed with an eight-hop limit and a check that the
target never leaves the volume root. If `.DirIcon` is missing or unresolvable, the largest PNG or JPEG under the
root and `usr/share/icons` is decoded as a best-effort substitute.

### `.desktop` launchers

The launcher capability reads the `[Desktop Entry]` group, ignores localized `Icon[xx]` keys and other groups,
and resolves the `Icon=` value in this order:

1. An absolute path, or a path relative to the launcher, when it names a `.png`, `.jpg` or `.jpeg` file
   (the same extensions are tried when the value has no extension).
2. A plain file name looked up beside the launcher.
3. A theme name looked up in `<volume>/usr/share/icons/<theme>/<size>/apps` (and `mimetypes`), then the
   `hicolor` theme, then `<volume>/usr/share/pixmaps`. The volume is found by walking up from the launcher
   until a folder carries XDG icon data, so a launcher scanned from a mounted volume never resolves against the
   host's own theme.

Only a PNG or JPEG that decodes is published; an SVG, an XPM or a text file named `.png` produces no icon. The
launcher entry itself is bounded to 256 KiB and at most 32 candidate files are examined. The local path is
host-owned per the SDK contract: it is never logged, and the diagnostic trace records only the kind of reason
(for example "the launcher entry has no Icon key").

### `.app` bundles

A macOS `.app` is a directory, so its bytes are not a source this plugin can claim. Resolving
`Contents/Info.plist` → `CFBundleIconFile` → `Contents/Resources/*.icns` would need folder-level previews in the
host or a catalog-scanning step; that is deliberately left out here, and the issue that adds it inherits the
`.icns` reader above. A plain Mach-O executable, which is the only file-level source inside the bundle, has no
icon resource and keeps the explicit no-icon state.

## "No icon" is not a failure

The explicit empty artifact list leaves the item on its type icon and never affects the metadata. It covers:

- A file without a resource directory, without icon groups, or with groups that declare no payloads.
- A bitmap at an unsupported bit depth, a truncated bitmap, or a PNG that does not decode.
- An icon image whose decoded pixels carry no visible pixels at all.
- An encoded PNG above `MaxPngBytes`, or a source above `MaxIconSourceBytes`.
- A malformed `.icns` container, or one whose decodable chunks are all above the byte or pixel bound.
- An AppImage with a squashfs payload, a damaged ISO 9660 volume, or no `.DirIcon` and no usable image file.
- A `.desktop` entry without a usable `Icon` key, an unresolved theme name, or an icon that is not a PNG or JPEG.
- A plain ELF or Mach-O executable, and any `.app` bundle (whose bytes are a directory, not a source).

## Bounds

Every value is a hard ceiling so a crafted file cannot make the plugin allocate or decode without limit:

| Option | Default | Bounds |
|---|---|---|
| `MaxSourceBytes` | 512 MiB | Sources above this are not parsed by either capability |
| `MaxIconSourceBytes` | 64 MiB | Sources above this keep every metadata value but publish no icon; never above `MaxSourceBytes` |
| `MaxIconBytes` | 4 MiB | One icon group or one icon image resource |
| `MaxIndexedIconImages` | 64 | Images inspected while looking for one that decodes |
| `MaxPngBytes` | 1 MiB | Encoded preview; a larger result is treated as no icon |
| `MaximumIconPixels` | 256 × 256 | Decoded raster; a larger header is refused before any row is decoded |
| `IconSize` | 128 | Edge of the encoded PNG, clamped to 16–128 |
| `MaxMetadataValueCharacters` | 512 | Each published text value |

Besides the host limits, each container has its own internal ceilings:

| Source | Ceiling |
|---|---|
| `.icns` | 256 chunks walked, then at most `MaxIndexedIconImages` payloads decoded, each at most `MaxIconBytes` |
| AppImage | 4 MiB probed for the volume descriptor (first 512 KiB, then the rest), 8 symlink hops, 48 folders and 64 image files scanned, each at most `MaxIconBytes` |
| `.desktop` | 256 KiB entry, at most 32 candidate files and 32 size folders examined, each image at most `MaxIconBytes` |

An AppImage above `MaxIconSourceBytes` keeps the no-icon state like any other oversized source.

The metadata capability also reads the icon group only to count images and report the largest declared size; it
does not decode pixels.

## Deliberately unsupported

Rejected instead of approximated, because a wrong icon is worse than a type fallback:

- Icon bitmaps whose header is neither a `BITMAPCOREHEADER` nor a supported `BITMAPINFOHEADER` size.
- Icon bitmaps above 256 × 256 pixels.
- Named resources and named languages: the plugin selects the neutral language first, then the lowest numeric
  language id, which is the convention icon groups use.
- Legacy `.icns` RLE chunks (`is32`, `il32`, `ih32`, `it32`) and the 24-bit `ic04`/`ic05` chunks: they store
  packed bitmaps this plugin does not approximate.
- SVG and XPM launcher icons: neither can be decoded into the bounded PNG preview without adding an XML or
  raster reader for a format that would render a wrong image if approximated.
- AppImage squashfs (type 2) payloads: no squashfs reader with a policy-compliant licence is available, so the
  whole container reports no icon instead of guessing.
- `.app` bundles: `Contents/Info.plist` → `CFBundleIconFile` → `Contents/Resources/*.icns` needs a folder-level
  preview path in the host; the `.icns` reader it would use already exists.

## Failure behaviour

Two custom exceptions carry fixed messages and one parameterized detail:

| Exception | Raised when |
|---|---|
| `ExecutableReadException` | A header, section table, directory node or payload does not fit the image or is truncated |
| `ExecutableIconException` | An icon resource is not a supported bitmap or PNG, or the encoded PNG exceeds its bound |

Both capabilities turn them into "no values" / "no artifact" and leave the rest of the metadata intact. Host
budget exhaustion and cancellation still propagate, so a cancelled scan stops instead of degrading silently.

## Dependencies and licensing

`dotnet list Plugins/SkyCD.Plugin.Metadata.Executable/SkyCD.Plugin.Metadata.Executable.csproj package`:

| Package | Version | License | Use |
|---|---|---|---|
| SkiaSharp | 3.119.4 | MIT | Decode embedded PNG/JPEG icons and encode the bounded preview |
| SkiaSharp.NativeAssets.Win32 / Linux / macOS | 3.119.4 | MIT | Native renderer for each RID |
| DiscUtils.Iso9660 | 0.16.13 | MIT | Read the ISO 9660 volume a type 1 AppImage appends to its runtime |

SkiaSharp and DiscUtils.Iso9660 are MIT dependencies that already pass the Trivy license gate in
`docs/legal/dependency-policy.md` and are used by the Image, EBook, Audio and DiskImages plugins, so no
policy change is needed.

The PE/COFF header and section-table facts are read with
`System.Reflection.PortableExecutable.PEHeaders`, which ships in-box with .NET 10 (`Microsoft.NETCore.App`, MIT)
and therefore adds no package, SBOM component or policy entry. This replaced the hand-written optional-header
parser; the layout comes from the BCL reader, including the standard section-table fields
(`PointerToRawData` at offset 20). The Win32 resource directory walk, the `VS_VERSION_INFO` parser, the bitmap
icon decoder and the box filter remain in this assembly because the BCL exposes no Win32 resource reader.

PeNet 6.1.2 (Apache-2.0, published 2026-05, `System.Security.Cryptography.Pkcs` 10.0.7 dependency) was evaluated
and not adopted: it parses a complete in-memory buffer or a synchronously read stream, so reading through the
host's bounded asynchronous `IMediaSource` would need a blocking adapter and either a whole-file buffer or a
second resource-directory walk beside the icon pipeline. Its licence metadata is Apache-2.0 rather than the MIT
assumed by the issue; both pass the Trivy license gate, so licence compliance was not the deciding factor and
no policy change is needed. Nothing is extracted to disk and no Windows-only API is used.

## Tests

`tests/SkyCD.Plugin.Metadata.Executable.Tests/` builds synthetic PE images in memory — DOS stub, PE header, one `.rsrc`
section with a packed three-level resource tree, a `VS_VERSION_INFO` block and icon bitmaps or PNGs — so no binary
fixture is checked in. The tests cover:

- The descriptor: extensions, MIME types, schema ids and `IndexTimePreview`.
- Header facts for PE32+, PE32, x86, a .NET image and a bare `mz-16` image.
- Header failure modes that must not escape the plugin contract: a truncated PE header or section table, an
  unsupported optional-header magic, zero sections, zero or overflowing data-directory counts, and a DOS image
  whose stub does not reach a PE signature.
- Version strings, the two length styles, and text truncation.
- Icon selection (largest image wins, damaged group falls back to the next group), DIB and PNG icons, alpha and
  AND-mask transparency, and the square bounded output.
- Every "no icon" case: plain text, `MZ`-only, no resources, truncated image, group without icons, empty payload,
  damaged PNG, garbage bitmap, oversized source, oversized PNG and clamp of the configured edge.
- A structurally valid PNG whose header declares 4096 × 4096 pixels is refused by the header bound before any
  raster is allocated (decompression-bomb protection).
- Option validation and the `MaxIconSourceBytes` ≤ `MaxSourceBytes` rule.
- `PlatformIconPluginTests` builds synthetic `.icns` containers, ISO 9660 volumes and AppImage prefixes in
  memory and covers: the largest icns chunk winning, an oversized chunk being skipped for the next one, the
  chunk byte bound, malformed containers, `.DirIcon` extraction, the themed-icon fallback, a type 2 payload,
  a damaged volume, and plain ELF and Mach-O producing no artifact.
- `PlatformIconPluginTests` also writes temporary `.desktop` files and covers: absolute, sibling and relative
  icon paths, a theme name resolved from a volume layout, an unresolved theme name, localized keys and desktop
  actions, and a text file named `.png`.
