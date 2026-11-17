# Issue #429: Typed Advanced-Search Queries and Indexed Catalog Filtering

Parent: #422. Extends basic Find (#417) without duplicating it: `CatalogFindService`
keeps serving the quick path/contributed display-field search, while the new
`SkyCD.Presentation.Search` layer adds versioned, repository-backed typed queries.

## Contract (`AdvancedCatalogQuery`, version 1)

- `Version` must equal `AdvancedCatalogQuery.CurrentVersion` (1); anything else throws
  `AdvancedCatalogQueryException`.
- `Scope`: `RootId` (null = whole catalog) with `IncludeDescendants`, plus optional
  `Types` to limit to entry kinds (e.g. media scope). Covers scope by media/folder/catalog.
- `Root`: a `CatalogQueryGroup` tree combining predicates with `All` (AND) / `Any` (OR);
  an empty root group matches everything in scope.
- `Skip`/`Take`: pagination, `Take` clamped to 1..1000 (default 200).
- Predicate fields: `Name`, `FullPath`, `Extension`, `DocumentType`, `SizeBytes`,
  `Status`, `CustomProperty` (keyed into entry `Properties`), `Metadata` (keyed into the
  raw typed metadata lookup). `Key` is required exactly for the two keyed fields.
- Evaluation input is active catalog data (`CatalogSearchIndex`, rebuildable from a
  repository `GetAll()`), never just loaded UI nodes.

## Semantics (stored values only)

- Text (`Contains`, `Equals`, `NotEquals`, `StartsWith`, `EndsWith`, `OneOf`): ordinal
  case-insensitive comparison on stored values. Display formatting (localized booleans,
  grouped numbers, `g` dates, appended units) is never parsed or compared.
- Missing values (null/whitespace): match only `IsMissing`; every other operator returns
  false for them. `IsPresent` is the inverse.
- Dates: compared as instants (`DateTimeOffset.UtcDateTime`); offsets and time zones do
  not affect equality or ordering. Strings parse with invariant culture only.
- Numbers: raw stored numbers compare directly with invariant parsing. Byte units
  (B/KB/MB/GB/TB) convert; any other unit must match exactly or the predicate is false.
- Unknown plugin/metadata keys evaluate safely: they match `IsMissing` and never throw,
  so unevaluated or future plugin fields cannot break a query.

## Indexing, ordering, cancellation

- `CatalogSearchIndex` precomputes identity, hierarchy, type, extension and status
  lookups; scope narrowing walks `childrenByParent` instead of scanning everything.
- `Upsert`/`Remove`/`Rebuild` keep the index consistent after edits and imports;
  full paths are lazily rebuilt and invalidated on mutation.
- Results order deterministically by full path (ordinal ignore-case) then id (ordinal).
- `CancellationToken` is honored during scope walk and per entry (checked every 256
  entries and on group recursion).

## Coverage

`tests/SkyCD.App.Tests/AdvancedCatalogSearchTests.cs` covers combined predicates,
nested scopes, media/folder scoping, missing data, unknown plugin fields, timezone
semantics, pagination stability, cancellation, index consistency after upsert/remove,
query validation, and a 5000-entry large-catalog case that prints the baseline
latency (about 4 ms for 1000 paged matches on the reference machine).
