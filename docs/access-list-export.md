# Microsoft Access list export

The assembly-discovered `skycd-access-list` capability is export-only for `.mdb`.
It creates a Jet 4-compatible database with one user table, `ItemList`, on Windows,
macOS and Linux. No Microsoft Access installation, ACE/Jet OLE DB provider, ADOX,
COM registration or native runtime is required.

Export is implemented with the pure-managed
[`JustyBase.UCanAccess.File`](https://www.nuget.org/packages/JustyBase.UCanAccess.File/)
1.1.0 package (Apache-2.0, .NET 10, no dependencies), a .NET port of Jackcess. The
package seeds new files from an embedded Access 2003 database template. The
template and the per-column text sort order use US English (LCID 1033), providing
Windows-1252-compatible code-page metadata while Jet 4 stores text as Unicode.

`AccessListExportRequest` carries the visible rows in insertion order and selected
`AccessListColumn` values in column order. Each column has its source key and
display label. Labels retain supported Unicode; invalid or empty names become
`ColumnN`, duplicates receive stable numeric suffixes, and names are limited to
Access's 64-character maximum.

All columns are `TEXT(255)` and required (`NOT NULL`). Zero-length strings are
accepted, so empty and null values are stored as empty strings. Numeric values use
invariant formatting, and timestamps use ISO-8601. Callers must supply raw byte
values for size columns. Values longer than 255 characters fail with an actionable
error instead of truncation.

Rows are streamed into the table in visible result order with bounded per-row
memory, progress, and cancellation between rows. Access tables do not define query
ordering: rows are inserted in visible result order, and no additional ordering
column is added to the user's selected schema.

Use `FileFormatManager.WriteFileAsync` for filesystem output. It stages a temporary
sibling and replaces the destination only after a successful export; CLI conversion
uses this route. The plugin builds its database in another temporary sibling and
streams it to the target only after successful completion. Failures and
cancellation remove temporary files and preserve the prior destination. Direct
stream callers own their stream's commit semantics.

Tests reopen the generated database with the file-format library on every platform
and verify the table name, column order, required flags, values and insertion
order. Where an ACE or Jet OLE DB provider is installed, an additional Windows test
uses ADO/ADOX to verify the file independently, including the stored LCID 1033
locale. The provider-dependent test skips with an explicit reason; no test requires
Microsoft Access or a legacy catalog application.
