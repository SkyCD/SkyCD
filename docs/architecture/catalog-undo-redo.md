# Catalog undo and redo: deletion, imports and preview retention

Implements #445 (part of #425), on top of the bounded history from #443, staged scan publication from #434,
multi-selection deletion from #406 and the media cache from #436/#437.

Code:

- `src/SkyCD.Core/UndoRedo/` — the generic bounded history, action contract and capacity notifications
- `src/SkyCD.Presentation/UndoRedo/` — catalog-specific manager, actions and cache pins
- `src/SkyCD.Presentation/ViewModels/MainWindowViewModel.cs` — deletion snapshots, undo/redo commands,
  retention reclaim
- `src/SkyCD.Presentation/Staging/CatalogScanPublisher.cs` — publication reporting whether undo was recorded

## What is reversible

| Operation | Action | Reverses as |
|---|---|---|
| Delete from the browser (single or multi-selection) | `DeleteCatalogAction` | one entry per deletion |
| Delete a tree node | `DeleteCatalogAction` | one entry per deletion |
| Published scan / import | `PublishScanAction` | one entry for the whole publication |
| Rename, status, comment, single property | the field-level actions | one entry per edit |

Only catalog records are affected. **No deletion path touches the filesystem** — `File.Delete` and
`Directory.Delete` do not appear in the deletion code, and a test deletes a catalogued file's entry and
asserts the file is still on disk afterwards.

## Deletion restores the whole subtree

The bug this issue exists for: browser deletion expanded the selection to every descendant, deleted all of
them, and then recorded **only the selection** for undo. Undoing a folder deletion restored the folder and
left its contents permanently gone. Tree deletion happened to collect descendants separately, and its
in-memory branch fell back to a placeholder document carrying only an id and a name.

Both paths now go through the same two helpers, and both return what they removed:

- `DeleteCatalogEntriesFromRepository` expands to descendants via `GetDescendantsOf`, snapshots every
  document, then deletes the id set inside one `Database.InBatch`.
- `DeleteCatalogEntriesFromInMemory` does the same against the in-memory list and returns the removed
  documents rather than discarding them.

The snapshot is read **from the repository**, not taken from the objects the UI was holding. The browser
carries overlay-modified copies (pending renames, added items), and undo has to restore what was stored.
Descendants come free from `GetDescendantsOf`, which already returns whole documents, so only the selected
entries cost an extra read.

Because a `CatalogDocument` is a single document carrying `ParentId`, `Properties` and `Status`, restoring it
restores the hierarchy position, the id, every custom property, the tags (which live in
`Properties["Tags"]`) and the status decoration in one write.

### Static collection memberships

There is no static-collection or list-membership feature in v3. The complete set of persisted document types
is `CatalogDocument`, `StatusVariantDocument`, `AppOptionsDocument`, `PluginDocument` and
`PluginConfigurationDocument`, plus the staging and media-cache collections; none of them is a join or
membership record. "Add To List" is the legacy *add-to-catalog* dialog, not a membership list.

So membership restoration is satisfied structurally rather than by extra code: the only relationships an
entry participates in are its `ParentId` and the contents of its own document, and both are inside the
snapshot. If a membership feature is added later, the requirement it has to meet is that membership lives on
the catalog document or in a record keyed by the entry id that survives deletion — otherwise it needs its own
snapshot in `DeleteCatalogAction`.

### Ids and the media cache fingerprint

Restoring the **original ids, names and ancestor chain** is not cosmetic. `MediaSourceFingerprint.ForCatalogEntry`
derives a cache key from the root ancestor's id, the name path, the size and `Properties["LastWriteTime"]`.
A restore that minted new ids or lost a level of the chain would orphan every cached preview of the subtree
silently, and the browser would show "not captured" for entries that were captured.

## Redo does not follow the selection

`DeleteCatalogAction.RedoAsync` replays the **captured ids**, never the current selection. On top of that,
`Undo` and `Redo` clear `SelectedBrowserItem`/`SelectedBrowserItems` before refreshing, because the restored
records are different instances from the ones the browser still holds, and `GetSelectedBrowserItems` has a
name-based fallback that can attach a stale selection object to a live item. A test deletes a folder, undoes
it, selects an unrelated entry, then redoes — and asserts the original subtree is gone while the unrelated
entry survives.

Undo and redo each run inside a single `Database.InBatch`, so a failure part-way through does not leave half a
subtree restored.

## Bounded snapshots and the footprint budget

A bounded history has to bound what it *holds*, not only how many entries it keeps: one action that snapshots
a large subtree outweighs a hundred renames. So:

- `IUndoableAction.EstimatedFootprintBytes` (default interface member, so existing implementers are
  unaffected) reports what an action retains.
- `DeleteCatalogAction` reports `UndoFootprint.ForDocuments(snapshot.Count)`; `PublishScanAction` reports its
  inserted documents plus **both** versions of every merged one; `UndoTransaction` reports the sum of its
  children.
- `UndoRedoHistory.MaxActionFootprintBytes` (0 = unlimited, which is the default and preserves existing
  behaviour) refuses an action above the budget. `PushAction` returns `false` and disposes the action.

`UndoFootprint.BytesPerDocument` is a deliberate order-of-magnitude allowance, not an accounting figure: the
budget exists to stop a large import being recorded as undoable, not to track bytes exactly.

A refusal is reported, never hidden. `ScanPublishResult.IsUndoRecorded` says whether a publication was
recorded, and the deletion paths set a distinct status message.

## Capacity behaviour and user feedback

Previously the history **silently disposed and dropped** its oldest entry when full. There was no event, no
return value and no pre-flight check, so a caller could not know an operation had stopped being reversible.

Now:

- `ActionEvicted` fires after an entry is dropped, carrying its display name and the remaining undo count.
- `ActionRejected` fires when an action is refused for exceeding the footprint budget.
- `MainWindowViewModel` subscribes to both and reports them: `text.HistoryCapacityReached`,
  `text.OperationNotUndoable`, and `text.DeletedNotUndoable` in place of the ordinary deletion message.
- `Undo`/`Redo` wrap the call in try/catch and report `text.UndoFailed` / `text.RedoFailed`. Previously an
  exception from a storage failure escaped the relay command into the dispatcher. On failure the history has
  already put the action back on its original stack, so the command stays offerable rather than silently
  losing the entry.
- Success reports what was reversed (`text.UndoCompleted` / `text.RedoCompleted` with the action name) rather
  than the bare word "Undo".

All strings ship in English and Lithuanian.

**Atomicity.** A deletion is one action covering the whole subtree, applied in one batch. An import is one
action covering every inserted and merged document. Scan publication now writes all chunks inside one
Couchbase Lite `InBatch` transaction. Failure or cooperative cancellation rolls back every document,
including earlier chunks, and leaves undo history unchanged. An undo action is pushed only after commit.

## Preview artifacts and history

Deleting an entry orphans its media cache records, but the entry is not really gone while history can restore
it — and restoring it restores the exact id, name and ancestor chain the fingerprint is built from.
Reclaiming the artifacts in between would leave the restored entry pointing at previews that no longer exist.

`CatalogHistoryCachePins` is a reference-counted registry of entry ids that history still holds:

- `DeleteCatalogAction` pins its snapshot's ids on construction and releases them in `Dispose(bool)`.
- Disposal is the eviction hook: the history disposes actions when it evicts them, when the redo stack is
  cleared, when history is cleared and when it is disposed. So a pin lives exactly as long as the operation
  that could restore it.
- Pins are reference counted because two overlapping deletions can hold the same descendant; only the last
  release unpublishes it.
- `MainWindowViewModel.ReclaimUnreferencedPreviewArtifacts()` runs `EnforceRetention` with
  `entryExists: id => pins.IsPinned(id) || catalog.Get(id) is not null`, so a pinned entry is not an orphan.

Reclamation is **deferred**, not inline. Eviction happens during a deletion, and running a cache scan on the
UI thread at that moment would stall the interaction that triggered it, so eviction sets a pending flag and
the next `Refresh` performs the pass. It runs with `CompactAfterCleanup = false`: deleting the records is
cheap, while `PerformMaintenance(Compact)` — the step that actually reclaims blob files — belongs to explicit
maintenance rather than a UI-thread refresh.

`Dispose` on the view model now disposes the undo manager (it previously leaked, along with every snapshot it
held), and `CompleteNewCatalog`/`CompleteOpenCatalog` reset history and release the pins. Carrying a previous
catalog's history into a newly opened one would let Undo write records into a database they no longer belong
to.

## Limitations

- Snapshots are full in-memory documents. The footprint budget bounds a single action but the history has no
  aggregate byte ceiling, only an entry count; spill-to-staging-store is not implemented.
- Publication atomicity is per batch, as described above.
- The four field-level actions (`Rename`, `ChangeStatus`, `ChangeComment`, `ChangeProperty`) re-read the live
  document and silently no-op when it is absent. That was harmless while deletion was not really undoable; now
  that it is, undoing a rename of an entry that has since been deleted and not yet restored is a no-op rather
  than an error. Ordering interactions between field-level edits and subtree deletions are not resolved here.
- History is per view-model instance and is not persisted. Reopening a catalog starts with empty history;
  this is asserted rather than left implicit.
- Preview reclamation waits for the next refresh. Artifacts can outlive their pin until then, which is the
  safe direction.

## Testing

`tests/SkyCD.App.Tests/CatalogDeletionUndoIntegrationTests.cs` runs against a real Couchbase Lite database in
a temp directory with a seeded four-level catalog, and covers: nested folder deletion restoring ids, parent
links, size, status, tags and custom properties; multi-selection deletion of two subtrees; tree-pane
deletion; redo after an unrelated re-selection; two full undo/redo cycles; a published import reversed as one
operation; an import too large for the footprint budget reporting `IsUndoRecorded = false` while still being
published; history eviction releasing pins and notifying; a deletion refused by the budget reporting
`DeletedNotUndoable` and leaving Undo disabled; preview artifacts surviving retention while pinned and being
reclaimed after history is cleared; artifacts surviving an undo/redo cycle; a storage failure (database
closed) during undo reporting `UndoFailed` without escaping the command and leaving the action on the stack;
deletion and undo both persisting across catalog reopen with empty history afterwards; and a catalogued file
still existing on disk after its entry is deleted.

`CatalogHistoryCachePinsTests.cs` covers the registry directly: pinning, overlapping owners releasing in
turn, no spurious release events, duplicate and blank ids, unknown owners, `Clear`, snapshot semantics of
`PinnedEntryIds`, and argument validation.

`UndoRedoHistoryTests.cs` gained coverage for the footprint budget (rejection, unlimited default, totals
across both stacks, transaction summation, negative budget) and for eviction raising an event and disposing
the dropped action.
