# Microsoft Access list export

The assembly-discovered `skycd-access-list` capability is export-only for `.mdb`.
It creates a Jet 4-compatible database with one user table, `ItemList`, without
requiring Microsoft Access or a legacy catalog application. Windows must have
ADOX/ADO and a Jet or ACE OLE DB provider matching the SkyCD process architecture.
The plugin tries registered ACE 16, ACE 12, then Jet 4 providers. No native runtime
is bundled and no additional NuGet packages are required.

`AccessListExportRequest` carries the visible rows in insertion order and selected
`AccessListColumn` values in column order. Each column has its source key and
display label. Labels retain supported Unicode; invalid or empty names become
`ColumnN`, duplicates receive stable numeric suffixes, and all identifiers are
bracket-quoted, including Access keywords.

All columns are `TEXT(255) NOT NULL` with zero-length strings enabled. Null values
become empty strings, numeric values use invariant formatting, and timestamps use
ISO-8601. Callers must supply raw byte values for size columns. Values longer than
255 characters fail with an actionable error instead of truncation. Each row uses
positional parameters; apostrophes and other data never enter SQL text.

The default compatibility option sets the Jet database locale to US English
(LCID 1033), providing Windows-1252-compatible locale metadata while retaining
the provider's Unicode text support. Database creation uses Jet engine type 5.
See Microsoft's [Jet provider properties](https://learn.microsoft.com/en-us/office/client-developer/access/desktop-database-reference/microsoft-ole-db-provider-for-microsoft-jet)
and [ADOX Create method](https://learn.microsoft.com/en-us/office/client-developer/access/desktop-database-reference/create-method-adox).

Use `FileFormatManager.WriteFileAsync` for filesystem output. It stages a temporary
sibling and replaces the destination only after a successful export; CLI conversion
uses this route. The plugin builds its native database in another temporary sibling
and streams it to the target only after successful completion. Failures and
cancellation remove temporary files and preserve the prior destination. Direct
stream callers own their stream's commit semantics.

Rows stream through a prepared parameterized command inside a native transaction,
with bounded per-row memory, progress, and cancellation between rows. Access tables
do not define query ordering: rows are inserted in visible result order, and no
additional ordering column is added to the user's selected schema.

Tests independently reopen generated databases with ADO to verify the schema,
values, and insertion order. Native tests skip with an explicit reason where the
matching runtime is unavailable; normalization and atomic-host-failure tests run
everywhere.
