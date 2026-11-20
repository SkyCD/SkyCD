# DMG disk image indexing plugin

Implements #722 on the file-format and indexing capability contracts.

Assembly: `Plugins/SkyCD.Plugin.Dmg/` → plugin id `skycd.dmg`.
Tests: `tests/SkyCD.Plugin.Dmg.Tests/`.

## What it provides

One read-only capability instance, discovered through assembly metadata and implemented capability types:

| Capability | Type |
|---|---|
| Catalog hierarchy | `DmgImageIndexPlugin : IFileFormatPluginCapability` |
| Index work items | `DmgImageIndexPlugin : IIndexWorkItemProvider` |

The plugin claims extension `.dmg` with MIME type `application/x-apple-diskimage`.

## Reading the image

The UDIF container is parsed in managed code with DiscUtils, so indexing works on Windows, macOS,
and Linux without `hdiutil` or any other native dependency.

- The `koly` trailer is validated first: signature, header size, property-list bounds, and sector
  count must all be consistent, so truncated or renamed files are rejected with a damage message.
- Data fork blocks are decoded through DiscUtils, which handles raw, zero, zlib, ADC, bzip2, and
  LZFSE block types.
- HFS+, FAT, and ext2/3/4 volumes are opened through DiscUtils. A DMG whose file system is stored
  directly in the data fork (the common case) is probed as a whole disk, and several volumes are
  prefixed with their volume label.
- Unsafe paths (`.`/`..`, control characters), reparse points, device entries, damaged entries, and
  overly deep trees are skipped instead of failing the whole image.

## Indexing

When metadata-collecting plugins are enabled, each contained file becomes an `IndexWorkItem`. The
plugin extracts one requested file at a time into a `DeleteOnClose` temporary file, serializes
concurrent extractions, and deletes the stream when the host disposes it. The temporary directory is
removed when the item set is disposed, including on failure or cancellation. Entry, per-file, and
total extraction limits bound what is materialized.

## Deliberately unsupported

These are rejected with a clear message rather than misread:

- Encrypted DMGs (the `encrcdsa` layout), which cannot be read without a password.
- Segmented images (`.dmgpart` sets), which are not joined.

## Tests

`tests/SkyCD.Plugin.Dmg.Tests/` builds UDIF images in memory around a FAT volume that DiscUtils
creates, including raw, zlib-compressed, and zero-block data forks, then exercises catalog listing,
one-file-at-a-time extraction, cleanup, limits, cancellation, and the damaged, encrypted, and
segmented rejection paths. `Fixtures/catalog-sample.dmg` also feeds the shared
`FileFormatCompatibilityTests`.
