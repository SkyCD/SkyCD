# Executable metadata and icon plugin

Implements #733 on the capability contracts from #435 (`docs/plugins/media-capabilities.md`).

Assembly: `Plugins/SkyCD.Plugin.Metadata.Executable/` → plugin id `SkyCD.Plugin.Metadata.Executable`.
Tests: `tests/SkyCD.Plugin.Metadata.Executable.Tests/`.

This plugin claims Windows executables and reads two things from them: the version information Windows shows in
a file's properties, and the program's own icon, which it stores as a PNG during indexing so the catalog can show
it later without the file being present.

## What it provides

Two independently discovered capabilities in one assembly:

| Capability | Type | Schema |
|---|---|---|
| Executable facts and version strings | `ExecutableMetadataPlugin : IMediaMetadataCapability` | `skycd.executable.metadata` v1 |
| Bounded icon preview | `ExecutableIconPlugin : IMediaPreviewCapability` | `skycd.executable.icon` v1 |

Discovery uses assembly metadata and implemented capability types only — there is no `plugin.json`.

The preview descriptor is marked `IndexTimePreview`, so the indexing pass stores the icon in the catalog together
with the metadata. The browser then renders it from the cached artifact, which means the icon survives the media
going offline, and reopening the item never reads the program again.

Default extensions are `.exe`, `.dll`, `.scr` and `.com`, with the common Windows executable MIME types
(`application/vnd.microsoft.portable-executable`, `application/x-msdownload`, `application/x-msdos-program`,
`application/x-dosexec`).

**Nothing is extracted to disk and no file is modified.** The headers, the resource directory nodes and the icon
payload are read through the host's bounded `IMediaSource`; only the bytes actually parsed are charged to the
source budget.

## Supported files

| Source | Metadata | Icon |
|---|---|---|
| PE32 and PE32+ images (`.exe`, `.dll`, `.scr`) | Header facts, version resource, icon facts | Yes, from `RT_GROUP_ICON` / `RT_ICON` |
| Bare DOS executables (`MZ` without a reachable PE header, e.g. `.com`-style stubs) | `Container = mz-16` only | No |
| A PE image with no resource directory | Header facts | No |
| A damaged header, section table or resource directory | Whatever was read before the damage | No |
| Any other file | No values | No |

Recognition is structural: the file must start with `MZ`, and a PE image must also carry the `PE\0\0` signature
the DOS stub points at. The extension is not trusted.

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

## "No icon" is not a failure

The explicit empty artifact list leaves the item on its type icon and never affects the metadata. It covers:

- A file without a resource directory, without icon groups, or with groups that declare no payloads.
- A bitmap at an unsupported bit depth, a truncated bitmap, or a PNG that does not decode.
- An icon image whose decoded pixels carry no visible pixels at all.
- An encoded PNG above `MaxPngBytes`, or a source above `MaxIconSourceBytes`.

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

The metadata capability also reads the icon group only to count images and report the largest declared size; it
does not decode pixels.

## Deliberately unsupported

Rejected instead of approximated, because a wrong icon is worse than a type fallback:

- Icon bitmaps whose header is neither a `BITMAPCOREHEADER` nor a supported `BITMAPINFOHEADER` size.
- Icon bitmaps above 256 × 256 pixels.
- Named resources and named languages: the plugin selects the neutral language first, then the lowest numeric
  language id, which is the convention icon groups use.

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
| SkiaSharp | 3.119.4 | MIT | Decode embedded PNG icons and encode the bounded preview |
| SkiaSharp.NativeAssets.Win32 / Linux / macOS | 3.119.4 | MIT | Native renderer for each RID |

SkiaSharp is a policy-allowed MIT dependency already used by the Image, EBook and AudioCD plugins, so no license
whitelist entry or policy change is needed. The PE reader, the resource directory walk, the version-information
parser, the bitmap icon decoder and the box filter remain in this assembly; nothing is extracted to disk and no
Windows-only API is used.

## Tests

`tests/SkyCD.Plugin.Metadata.Executable.Tests/` builds synthetic PE images in memory — DOS stub, PE header, one `.rsrc`
section with a packed three-level resource tree, a `VS_VERSION_INFO` block and icon bitmaps or PNGs — so no binary
fixture is checked in. The tests cover:

- The descriptor: extensions, MIME types, schema ids and `IndexTimePreview`.
- Header facts for PE32+, PE32, x86, a .NET image and a bare `mz-16` image.
- Version strings, the two length styles, and text truncation.
- Icon selection (largest image wins, damaged group falls back to the next group), DIB and PNG icons, alpha and
  AND-mask transparency, and the square bounded output.
- Every "no icon" case: plain text, `MZ`-only, no resources, truncated image, group without icons, empty payload,
  damaged PNG, garbage bitmap, oversized source, oversized PNG and clamp of the configured edge.
- A structurally valid PNG whose header declares 4096 × 4096 pixels is refused by the header bound before any
  raster is allocated (decompression-bomb protection).
- Option validation and the `MaxIconSourceBytes` ≤ `MaxSourceBytes` rule.
