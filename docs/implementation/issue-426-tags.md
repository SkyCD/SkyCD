# Issue #426: Reusable tags and catalog-entry tag assignments

Parent: #421 (tags and virtual collections). This task covers persistence only;
browser UI (#427) and static virtual collections (#428) are separate.

## Storage

- `TagDocument` (`tags` collection): stable `Id` (`tag-<slug>`, suffixed on collision)
  and case-insensitively unique `Name`. Renaming edits the name only, so assignments
  referenced by id never orphan.
- Assignments live on each entry as a sorted id list in `Properties["Tags"]`. They
  travel with the native catalog document, survive repository recreation and restart,
  and vanish with the entry on delete, so entry deletion cannot orphan references.
- Entries written before tag storage carry no `Tags` property and read as untagged;
  migration (`CatalogTagService.NormalizeAllEntries`) only rewrites non-canonical
  values and never dirties an otherwise clean catalog.

## Operations (`CatalogTagService`)

Create/rename/merge/delete tags, per-entry get/set, batch assign/unassign across
entries (missing entries skipped), entries-with-tag lookup, dangling-reference
cleanup, and migration normalization. Unknown tag ids are rejected; duplicate names
(case-insensitive, trimmed) are rejected on create and rename. Merge unions
assignments onto the target and deletes the source; delete sweeps assignments from
every entry.

## Undo

`ChangeTagsAction` (single entry) and `ChangeTagsBatchAction` (entry map) restore
prior id lists; `CatalogUndoManager.RecordTagsChange` / `RecordTagsBatchChange`
mirror the status-change recording pattern (caller applies, then records).

## Export limitations

Tags round-trip through native catalog storage only. Row/CSV/XML export formats do
not carry tag assignments; re-importing an export loses tagging, which must be
re-applied from the native catalog.

## Tests

`tests/SkyCD.App.Tests/TagAssignmentTests.cs` covers legacy migration, persistence
across repository recreation, duplicate names, rename/merge/delete semantics,
batch ops, undo/redo, unknown-id rejection, and dangling-reference cleanup against
real Couchbase Lite databases in isolated temp directories.
