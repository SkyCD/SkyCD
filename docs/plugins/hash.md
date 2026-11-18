# P2P file-hash metadata plugin

Implements #446 on the capability contracts from #435 (`docs/plugins/media-capabilities.md`) and the
durable cache from #436.

Assembly: `Plugins/SkyCD.Plugin.Hash/` → plugin id `SkyCD.Plugin.Hash`.
Tests: `tests/SkyCD.Plugin.Hash.Tests/`.

## What it provides

| Capability | Type | Schema |
|---|---|---|
| Interoperable file identifiers | `HashMetadataPlugin : IMediaMetadataCapability` | `skycd.hash.metadata` v1 |

The provider claims every source through the `*` extension and MIME wildcard and is marked `IsOptIn`.
Discovery uses assembly metadata and implemented capability types only — there is no `plugin.json`.

### Automatic scan extraction

When the plugin is enabled and loaded (the default), the host hashes published scan files in the
background and reports progress in the status bar. Local scans record each file's source path and last
write time even without extended information. Current captures are reused on unchanged rescans; failures
do not affect publication. When the plugin is disabled or unavailable, scans do not hash files.

Hashing reads the whole file, so the provider stays opt-in: previews and other implicit extraction use
`MediaProviderSelection.Standard` and never trigger hashing. Only the scan-publication pass selects it.

Nothing in the plugin opens a peer-to-peer connection, resolves a magnet link, or publishes metadata to
any network.

## Metadata schema

| Key | Type | Meaning |
|---|---|---|
| `ed2k` | Text | eD2k file hash, lowercase hexadecimal (32 characters) |
| `tth` | Text | Tiger Tree Hash root, RFC 4648 base32 without padding (39 characters) |
| `sha256` | Text | Whole-file SHA-256, lowercase hexadecimal (64 characters) |

Each value names its algorithm, so a digest is never presented as a different kind of identifier.
In particular SHA-256 is **not** a BitTorrent info-hash and is never labelled as one.

## Algorithms

One bounded streaming pass feeds all three algorithms; memory use is independent of file size (the
provider requests at most 128 KiB at a time and the host bounds every read against the source budget).

- **eD2k** — MD4 per 9,728,000-byte chunk. A source of at most one chunk is the MD4 of its bytes;
  larger sources hash the concatenated chunk MD4s. A source ending exactly on a chunk boundary adds no
  trailing empty chunk.
- **TTH** — THEX tree over 1024-byte leaves: `Tiger(0x00 || leaf)` and `Tiger(0x01 || left || right)`,
  with an unpaired hash promoted unchanged. An empty file is one zero-length leaf, producing `Tiger(0x00)`.
  The result is displayed in the base32 form used by P2P clients.
- **SHA-256** — `System.Security.Cryptography.IncrementalHash`.

Failures and truncation abort the pass; incomplete digests are never published.

## Storage and display

Results are stored in the media cache from #436 as typed metadata under the source fingerprint, with
algorithm, value, fingerprint, capture timestamp and plugin provenance. They stay readable offline and
after the plugin is disabled, because reading the cache never requires the provider or the original file.

The Properties/preview pane shows the values with localized labels (`core.metadata.ed2k`, `tth`,
`sha256`) and per-field copy buttons. A failed extraction appears as a visible failed field instead of
looking never-computed, and an existing captured value is kept when a later hashing attempt fails.

## Dependency licence

`BouncyCastle.Cryptography` 2.7.0 supplies MD4 and Tiger. It is MIT-licensed, has no dependencies, and
is on the allowed list in `docs/legal/dependency-license-policy.json`.
