# Issue #430: Advanced-search filters, result sorting and source navigation

Parent: #422. Builds a reusable query editor/results view on #417 (quick Find)
and the shared #429 query service (`AdvancedCatalogQuery`,
`AdvancedCatalogSearchService`, `CatalogSearchIndex`).

## Presentation

- `AdvancedCatalogFilterRowViewModel`: one editable predicate. Field, operator,
  key (only for `CustomProperty`/`Metadata`), and text value. `OneOf` splits the
  value box on semicolons. Values stay in text form; the service parses numbers
  and dates with invariant culture, so the editor never interprets localized
  display strings. Incomplete rows report a reason instead of building.
- `AdvancedCatalogFindDialogViewModel`: AND/OR combination, entry-kind scope
  (all/media/folder/file), add/remove/clear/reset, validation with the row
  number, sortable paged results, cancellation, and navigation.
  - Search rebuilds `CatalogSearchIndex` from the current entry list on a
    background task, so re-evaluation observes current data. A generation
    counter plus `CancellationTokenSource` discards superseded passes, so a
    slow earlier search cannot replace a newer query; cancelling reports
    "Search cancelled." and keeps the previous results. Unexpected failures
    surface as "Search failed: ..." instead of faulting the command.
  - The service returns up to 1000 ordered matches; the view model sorts them
    by full path (default), name, size, type, or status over stored values and
    pages them in hundreds. Truncation is reported in the status line.
  - Metadata lookup and discovered keys come from the caller: `MainWindow`
    builds a raw typed lookup from media-cache records through
    `MediaEntryMetadataProjection` (captured values first, user annotations
    winning), plus metadata keys from record schemas and captured keys,
    custom-property keys from entry properties, and status values. An empty
    record store yields no lookup and no keys.
- `AdvancedCatalogFindWindow` (`SkyCD.App`): filter grid with field/operator/
  key/value columns, combination and scope selectors, sort and pager controls,
  virtualized results with name/path/type/status context, status line, and
  Open/Close. Double-click or Open navigates to the original entry through the
  same `NavigateToFindResult` path as quick Find, so activation never moves or
  copies catalog nodes. Simple Find is untouched: the Edit menu gains a separate
  "Advanced find..." item next to Find (`Ctrl+F`).

## Limitations

- Tag filtering: tags live in `Properties["Tags"]` as id lists, so they appear
  among discovered custom-property keys but only single-string values match
  the text operators; matching list-valued tags needs a future service rule.
- Result pages are client-side over the first 1000 service matches; larger
  catalogs report truncation in the status line.

## Tests

`tests/SkyCD.App.Tests/AdvancedCatalogFindDialogTests.cs` covers AND/OR
composition, no matches, empty-filter list-all, validation blocking with retry,
superseded-search generation guard, cancel keeping previous results, paging
(250 entries over 3 pages), sorting, kind scoping, custom-property and
metadata search with key discovery, navigation callback, and clear/reset. The
sandbox testhost cannot run xUnit here (`Win32Exception: Access is denied` in
testhost startup), so the same scenarios were additionally verified with a
standalone harness against the real assemblies (13/13 checks pass); CI owns
the xUnit run.
