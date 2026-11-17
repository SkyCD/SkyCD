# XML catalog and list export (`skycd-xml-catalog`, `skycd-xml-list`)

Export-only capabilities in `SkyCD.Plugin.XmlExport` that write the current
catalog selection or result set as UTF-8 XML 1.0. They never read:
`ReadAsync` reports write-only. Routing uses the explicit format ids
(for example `convert --format skycd-xml-catalog`); the `.xml` extension is
shared with the `skycd-xml` round-trip format, which keeps its own
`urn:skycd:catalog` schema.

## Request

`XmlExportRequest` carries rows plus document-level configuration:

- `Rows`: the items to export. Hierarchical exports link rows through
  `nodeId`/`parentId` keys and preserve input order at every level; flat-list
  exports emit rows in enumeration order. Any
  `IEnumerable<Dictionary<string, object?>>` works. A bare row list exports
  with defaults.
- `DatabaseName`: catalog name written to the root `Name` (hierarchical) or
  `Database` (flat list) attribute. Defaults to the output file name without
  extension, else `Catalog`.
- `Stylesheet`: optional XSL stylesheet reference, emitted as an
  `xml-stylesheet` processing instruction. Must be a bare relative `.xsl`
  file name: no directories, no absolute paths, no traversal. Anything else
  fails the export with an `XmlExportInvalidValueException` naming the
  `Stylesheet` field.
- `Columns`: selected flat-list columns in exact export order. Flat-list
  profile only; when null, every non-structural row key exports in row order.

Row keys resolve case-insensitively. Structural keys are `nodeId`,
`parentId`, and `kind`; the hierarchical profile additionally treats `name`,
`size`/`sizeBytes`, and date keys (`date`, `modified`, `modifiedUtc`,
`lastModified`, `lastModifiedUtc`) as structural.

No NuGet package is involved: both writers stream through `System.Xml.XmlWriter`
from the .NET 10 base class library.

## Hierarchical catalog profile (`skycd-xml-catalog`)

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Broken_X_Database Name="Catalog name">
  <Disk Name="Media" Size="123" Date="2026-11-13T10:15:30Z">
    <Folder Name="Docs" Size="123" Date="2026-11-13T10:15:30Z">
      <File Name="readme.txt" Size="123" Date="2026-11-13T10:15:30Z" />
    </Folder>
  </Disk>
</Broken_X_Database>
```

Entry kinds map to element names: `disk` to `Disk`, `folder`/`directory` to
`Folder`, `file` to `File`; any other kind normalizes to a valid element
name (`tape archive` becomes `tape_archive`), falling back to `Item`. Every
item has a `Name`; a missing or blank name fails the export naming the item.
`Size` renders as unsigned decimal bytes (missing values default to `0`;
negative or non-integer values fail). `Date` renders as ISO-8601 in UTC
(`2026-11-13T10:15:30Z`, fractional seconds kept when present) and is
omitted when unknown; unparseable values fail. Every other row key becomes a
child `<Metadata Name="..." Value="..." />` element; null values are skipped.

Orphan rows whose `parentId` matches no exported `nodeId` (and rows without
parents) export as roots, so selected descendants keep their hierarchy.

## Flat list profile (`skycd-xml-list`)

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<ItemList Database="Catalog name">
  <Item Type="File">
    <Name>readme.txt</Name>
    <Size>123</Size>
    <Modified>2026-11-13T10:15:30Z</Modified>
  </Item>
</ItemList>
```

Each row becomes an `Item` whose `Type` comes from `kind` (`disk`/`folder`/
`file` map to `Disk`/`Folder`/`File`; missing kinds use `Item`). Child
elements follow the `Columns` order, or row-key order for bare row lists.
Canonical renames apply without a `Label`: `name` to `Name`, `size`/
`sizeBytes` to `Size`, date keys to `Modified`. Other keys normalize to
valid element names and carry the original label in `Label` when
normalization changes it (`File Size` becomes
`<File_Size Label="File Size">`). Unknown requested columns export as empty
elements. An empty result writes a self-closing `<ItemList Database="..."/>`.

## Text safety

`&`, `<`, `>`, `"`, and `'` escape per XML rules (via `XmlWriter`), and
Unicode — including astral-plane characters — passes through. Characters
XML 1.0 cannot represent (control characters, lone surrogates) fail the
export with an `XmlExportInvalidCharacterException` naming the item and
field; `Size`/`Date` violations raise `XmlExportInvalidValueException` with
the same diagnostics. No whole-document DOM is ever built: hierarchical
assembly buffers row references only, and flat-list rows stream straight
through the writer. Progress reports percentages when the row count is known
and cancellation is honored between items (`OperationCanceledException`
propagates instead of becoming a failure result).

## Tests

`tests/SkyCD.Plugin.XmlExport.Tests/XmlExportTests.cs` covers registration as
export-only, both golden files above, XSD validation of both profiles,
ordering (child-before-parent, orphans as roots), empty outputs, Unicode and
escaping, metadata-rich rows, sizes beyond 4 GiB, unknown kinds and columns,
`Label` normalization, stylesheet acceptance and rejection (traversal,
absolute paths, wrong extension), filename-derived database names, all
invalid-character/size/date/name diagnostics, cancellation of both profiles,
and progress reaching 100.
