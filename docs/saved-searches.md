# Saved searches

Advanced Find can save a named filter definition, load it into the filter editor,
and update the selected saved search. Scope, combination, typed predicates and
sort settings are persisted with the query version. Rename and delete saved
searches through Manage Collections; they are identified as saved searches there.

Saved searches belong to the current catalog repository, just like static
collections. Opening a different catalog uses that catalog's collections. They
store no copied entries and do not change entries' parents or properties.
Opening or refreshing a collection evaluates its query against current entries;
an open collection dialog also refreshes after catalog document changes.
Metadata uses the same current cache projection as Advanced Find.

A missing scope root produces no results. Missing custom-property or metadata
keys match only an explicit `IsMissing` predicate; other operators do not match
them. Losing a plugin schema does not remove or relax a saved predicate.
Fields and operators outside the supported typed query contract are rejected,
including unrecognized tag predicates. Saved queries with corrupt structure or
unsupported versions display an error and remain available for deletion.

The visual editor supports one flat predicate group and one entry-type scope.
It refuses to load more complex programmatically created queries instead of
flattening them or silently widening their scope. Evaluation retains the query's
bounded result limit (Advanced Find saves a limit of 1,000), ordered by the saved
sort settings before applying that limit.
