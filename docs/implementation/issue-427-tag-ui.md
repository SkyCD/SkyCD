# Issue #427: tag management and bulk tagging UI

Built on the #426 persistence layer (`TagDocument`, `TagDocumentRepository`,
`CatalogTagService`, undo actions). All tag mutations go through the service
and join the undo history; Cancel paths never touch the repository.

## Surfaces

- **Context menu, browser pane**: a `Tags` submenu (`edit_tags`) lists every
  reusable tag with a tri-state glyph (☑ all selected entries carry it,
  ◐ some do, ☐ none do). Activating a tag toggles it: fully assigned removes
  it from all selected entries, otherwise it is added to the ones missing it.
  Unrelated tags are never touched. The submenu rebuilds on every selection
  change (`RefreshTagsMenuItems`); with no tag service it shows one disabled
  item, and with no tags only the manager entry.
- **Tags manager dialog** (`TagsWindow` + `TagsDialogViewModel`): create,
  rename, merge (into another tag), and delete tags; per-tag usage counts;
  "Show items" filters through the shared advanced-search contract
  (`CustomProperty` `Tags` `Contains`) and lists matches with double-click
  navigation into the catalog tree/browser. All definition operations record
  undo actions; the dialog applies immediately and closes without further
  changes.
- **Properties dialog, Tags tab**: two-state checkboxes for a single entry,
  applied on confirm through `SetEntryTags` + `RecordTagsChange`; Cancel
  discards (assignment state lives only in the dialog view model).

## Search contract change

`AdvancedCatalogSearchService.MatchStoredValue` now matches multi-value
properties element-wise: any present item may satisfy the predicate, an
empty or all-missing sequence counts as missing, and `NotEquals` additionally
holds for missing values (persistence layers may drop empty collections, so
missing must count as "not equal"). `IsMissing`/`IsPresent` keep their
meanings. This is what makes `CustomProperty(Key="Tags", ...)` filtering
work; the manager dialog's "Show items" is the first consumer.

## Undo coverage

Assignment changes use the existing `ChangeTagsBatchAction` /
`ChangeTagsChange`; new `CreateTagAction`, `RenameTagAction`,
`DeleteTagAction`, `MergeTagsAction` (with `RecordTagCreated/Renamed/
Deleted/Merged` on `CatalogUndoManager`) cover definition changes, snapshotting
affected entries so undo restores tags and assignments exactly.

## Localization and accessibility

New `UiKeys`/`TextKeys` with English strings in `en-UI.yaml`/`en-Text.yaml`
(other locales fall back to English). Tri-state is conveyed by glyph plus
header text; all actions are `ICommand`s reachable by keyboard (menu
Enter/Space activation, dialog buttons and lists are tab-navigable).

## Tests

- `tests/SkyCD.App.Tests/TagDefinitionUndoTests.cs`: create/rename/delete/
  merge with undo/redo round-trips against real Couchbase repos.
- `tests/SkyCD.App.Tests/AdvancedCatalogTagSearchTests.cs`: multi-value
  matching semantics (Contains/OneOf/IsPresent/IsMissing/NotEquals/Equals).
- `tests/SkyCD.UI.Tests/TagManagementViewTests.cs`: headless UI — tri-state
  icons for mixed selection, bulk add/remove without touching unrelated
  tags, undo restoration, manager create/rename/merge/delete persistence,
  contract-based filtering with navigation, Properties tab two-state and
  cancel semantics, Enter-key menu activation, dialog requested event.
