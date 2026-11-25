# Issue #428: Persistent static virtual collections and collection navigation

Parent: #421 (tags and virtual collections). Static collections reference entry
ids; dynamic collections reuse the same presentation over saved advanced-search
queries (#429) evaluated on demand.

## Storage

- `VirtualCollectionDocument` (`collections` collection): stable `Id`
  (`<slug>`, suffixed on collision), case-insensitively unique `Name`, `Kind`
  (`Static` / `Dynamic`), `EntryIds` for static memberships, `SavedQueryJson`
  for dynamic collections.
- Membership references entries; removing a membership or a collection never
  deletes catalog entries, and entry documents keep their original ids, parents,
  and paths untouched.
- Deleted entries are skipped deterministically at resolve time with a missing
  count, plus explicit prune; no delete-flow hooks. Duplicate memberships are
  impossible (ids are normalized to a sorted distinct list on every write).

## Operations (`VirtualCollectionService`)

Create/rename/delete collections, `AddEntries` / `RemoveEntries` /
`SetEntryIds` (static only; dynamic collections throw `InvalidOperationException`
because membership is evaluated, not stored), `FindCollectionsContaining`,
`ResolveEntries` (static ids, or the saved query through the shared
advanced-search contract), `PruneDeletedReferences`, and JSON query round-trip
(`SerializeQuery` / `DeserializeQuery`, tolerant normalization). Unknown
collection ids throw `CollectionNotFoundException`; blank names throw
`CollectionNameRequiredException`; duplicate names (case-insensitive, trimmed)
throw `CollectionDuplicateNameException`.

## Presentation

- `CollectionsDialogViewModel` manages the same member list and navigation for
  both static and dynamic kinds: create/rename/delete, missing-reference
  cleanup, member navigation, and close. One class per custom exception with
  fixed message templates.
- `MainWindowViewModel` builds a `Collections` context submenu with one
  tri-state toggle per static collection (checked when all selected entries
  belong, partial when only some do), plus new-collection and manager entries.
  The toggle adds missing entries or removes existing ones without touching
  unrelated memberships; dynamic entries open the manager for navigation.
  Selection changes refresh the menu and toggle availability.
- `OptionsWindow` (`SkyCD.App`) hosts the dialog view model on the Collections
  settings page (issue #1068); the manager command opens Options preselected on
  that page. Navigation locates the original entry via `NavigateToFindResult`,
  so activation never moves or copies catalog nodes.
## Undo

`CreateVirtualCollectionAction`, `RenameVirtualCollectionAction`,
`DeleteVirtualCollectionAction`, and `ChangeCollectionEntriesAction` (old/new id
lists) restore prior state; `CatalogUndoManager.RecordCollection*` mirrors the
tag recording pattern (caller applies, then records).

## Tests

`tests/SkyCD.App.Tests/CollectionAssignmentTests.cs` covers parent/path
preservation, persistence across repository recreation and restart, deleted and
ghost entries (skip + prune counts), duplicate storage, removal safety for
entries on membership/collection delete, name validation, rename stability,
dynamic evaluation and edit rejection, query JSON round-trip and persistence,
and membership undo/redo against real Couchbase Lite databases in isolated
temp directories. The sandbox testhost cannot run xUnit here
(`Win32Exception: Access is denied` in testhost startup), so the same scenarios
were additionally verified with a standalone harness against the real
assemblies (27/27 checks pass); CI owns the xUnit run.
