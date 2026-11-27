# Volume serial number plugin

Implements #642 on the shared medium-level capability contracts
(`src/SkyCD.SDK/Capabilities/Media/MediaTargetContracts.cs`).

Assembly: `Plugins/SkyCD.Plugin.Metadata.Volume/` → plugin id `SkyCD.Plugin.VolumeSerial`.
Tests: `tests/SkyCD.Plugin.Metadata.Volume.Tests/`.

## What it provides

| Capability | Type | Schema |
|---|---|---|
| Filesystem volume serial number | `VolumeSerialPlugin : IMediaTargetMetadataCapability` | `skycd.volume-serial` v1 |

Discovery uses assembly metadata and implemented capability types only — there is no `plugin.json`.
The plugin is independently enabled/disabled like every other plugin; when it is disabled or
unavailable, media scans simply never record a serial.

### Capture flow

When the plugin is enabled and **Include Media Info** is selected in the Add To List dialog, the disk
capture flow asks the shared media host seam (`IMediaTargetMetadataProvider`, implemented by
`MediaTargetExtensionManager` over enabled plugin capabilities) for volume facts and stores them as
properties of the new catalog media root — both when the media is added as a new item and when it is
added beneath a selected catalog item. The seam asks the plugin only about media it supports
(`IMediaTargetMetadataCapability.Supports` receives a `MediaTarget` whose kind is
`MediaTargetKind.Volume` or `MediaTargetKind.OpticalDisc` — a data disc is a volume too — and whose
`Location` addresses it). Volume facts are never forced through the file-byte
`MediaItem`/`IMediaSource` metadata contracts.

Capture uses ordinary read-only OS access (`GetVolumeInformationW` on Windows): no elevation and no
file content reads. The provider observes cancellation, and a failing or removed volume degrades to
"no value recorded" without breaking the scan. A genuinely reported zero serial is stored as
`0000-0000`; an API failure stores nothing, so a failure is never shown as a real serial and a later
background refresh can never erase a previously captured serial with a placeholder.

## Metadata schema

| Key | Type | Meaning |
|---|---|---|
| `VolumeSerialNumber` | Text | Filesystem volume serial number, upper-case `XXXX-XXXX` hexadecimal |

### Value format

The value is the 32-bit filesystem volume serial exactly as the OS reports it, displayed in the same
lossless upper-case hexadecimal form the Windows `vol` command prints (`XXXX-XXXX`, 8 hex digits).
`VolumeSerialSchema.Format`/`TryParse` convert between the 32-bit value and the display text without
loss.

### Identifier limitations

- This is a **volume identifier**, not a physical drive manufacturer's serial number, not a
  cryptographically unique identity, and not a content fingerprint.
- The value may change when a volume is reformatted.
- Supported platform for capture: Windows. Other platforms degrade gracefully and record no serial.
- The captured serial is shown in the media's Properties (localized as *Volume serial number*) and
  persists with the catalog; it stays visible after the catalog is reopened and while the volume is
  offline.
- The serial is never used to change media matching or synchronization rules.
