# Issue #647: Physical storage location management

## Storage

- `LocationDocument` (`locations` collection): stable `Id` (`loc-<slug>`, suffixed
  `-2`, `-3`, ... on collision) and `Name`. Renaming edits the name only, so
  assignments referenced by id never orphan.
- Assignments live on each entry as `Properties["LocationId"]`. They travel with the
  native catalog document, survive repository recreation and restart, and vanish with
  the entry on delete, so entry deletion cannot orphan references.
- `CatalogDocument.Location` is a projected display name only; it is never persisted.

## Import canonicalization

- `CatalogLocationService.ImportSourcePropertyKeys` names the per-source property
  keys that carry a physical location name at import time: `PhysicalLocation` for the
  WinCatalog CSV/XML reports and `ZdcStorageLocation` for CD Catalog Expert (ZDC).
  The bare `Location` key is deliberately not aliased: other importers use it for
  filesystem paths.
- `CanonicalizeImportedLocation` trims the raw source value, resolves it to a
  `LocationDocument` (`EnsureLocation`), stores the stable id in `LocationId`, and
  keeps the raw source key in `Properties` untouched, so ZDC export round-trips the
  disc storage location (`L.DS`) byte-losslessly. Blank/whitespace-only values cannot
  be mapped and are reported per plugin in the import summary `SkippedReasons`
  (`Row N: blank Physical Location value was ignored.` / `Item 'name': ...` /
  `Disc 'name': blank storage location value was ignored.`) instead of being silently
  dropped.

## Resolution (`CatalogLocationService`)

A location assigned to a media/volume entry is inherited by its descendants; any
entry may carry its own override. Effective location resolution walks the parent
chain to the nearest assignment. Create/rename/merge/delete manage the location
documents; merge rewrites assignments to the target and delete sweeps assignments
from every entry, so no orphan references remain.

## In-use reporting

`CountUsages` counts entries whose effective location (own or inherited) is the
given location; `CountDirectUsages` counts direct assignments only. Both back the
confirmations shown before rename, merge, and delete.

## Export limitations

- WinCatalog CSV and XML are read-only (`CanWrite: false` / no export); locations
  assigned in SkyCD are never written back to those reports.
- ZDC export writes the retained `ZdcStorageLocation` back to `L.DS` unchanged, so
  the disc storage location round-trips losslessly; a blank `DS` present in the
  source is reported on import and omitted on save.

## Tests

`tests/SkyCD.Plugin.WinCatalog.Csv.Tests`, `tests/SkyCD.Plugin.WinCatalog.Xml.Tests`,
and `tests/SkyCD.Plugin.CdCatalogExpert.Tests` cover blank-value reporting and raw
value preservation on import; `tests/SkyCD.App.Tests` covers the location model
(persistence, inheritance, override, in-use reporting) against real Couchbase Lite
databases.
