# Related Format Support Tracker

This file tracks v3 format-support tickets created from issue `#118` ("Add related file formats support").

## Catalog formats

- `#75` JSON (`*.json`) read/write
- `#76` CSV (`*.csv`) read/write
- `#77` XML (`*.xml`) read/write
- `#78` YAML (`*.yaml`, `*.yml`) read/write
- `#79` TOML (`*.toml`) read/write

## Export formats

- `#80` HTML export (`*.html`) write-only
- `#81` Markdown export (`*.md`) write-only

## Archive index import formats

- `#82` ZIP (`*.zip`) read-only
- `#83` 7z (`*.7z`) read-only
- `#84` TAR/TAR.GZ (`*.tar`, `*.tar.gz`, `*.tgz`) read-only
- `#85` ISO (`*.iso`) read-only

## Third-party catalog report import formats

- `#448` WinCatalog CSV report (`*.csv`) read-only
- `#447` WinCatalog XML report (`*.xml`) read-only
- `#494` CdCat (`*.hcf`) read-only

## Third-party native catalog import formats

- `#486` WinCatalog native catalog (`*.w3cat`) read-only
- `#514` abeMeda/NeoFinder native catalog (`*.cdw`, `*.abeMeda7`, `*.neofinder7`) read-only
- `#645` DiskBase native catalog (`*.qdr`) read-only. Supports database version `0x0501` with version-1 tree and directory records; `0x0500` and older record layouts are rejected. Names and descriptions default to Windows-1252, because QDR stores no code-page identifier; callers using the plugin API can select another code page. Undefined byte sequences are rejected. DOS timestamps have two-second precision and an unknown timezone. Individual sizes contain only the stored 32-bit value, so sizes above 4 GiB cannot be recovered.

## Notes

- Tickets are grouped by behavior (read/write, write-only, read-only).
- Detailed acceptance criteria and implementation status are managed per-ticket.
