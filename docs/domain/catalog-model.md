# SkyCD v3 Catalog Model

## Purpose
Define the canonical file collection model for v3 and provide EF Core mapping guidance for SQLite persistence work in #63.

## Model Summary
- `Catalog`
  - Root aggregate for one indexed collection.
  - Tracks `SchemaVersion` for forward migration.
- `CatalogNode`
  - Represents both folders and files in one hierarchy.
  - Self-reference via `ParentId`.
  - `Kind` differentiates folder vs file.
- `CatalogTag`
  - Key/value metadata attached to a catalog.

## Entity Definitions
### Catalog
- `Id: Guid` (PK)
- `Name: string` (required, max 256)
- `SchemaVersion: int` (required, starts at `1`)
- `CreatedUtc: DateTimeOffset` (required)
- `UpdatedUtc: DateTimeOffset` (required)
- `Nodes: ICollection<CatalogNode>`
- `Tags: ICollection<CatalogTag>`

### CatalogNode
- `Id: long` (PK)
- `CatalogId: Guid` (FK -> Catalog)
- `ParentId: long?` (nullable FK -> CatalogNode)
- `Kind: CatalogNodeKind` (`Folder`, `File`)
- `Name: string` (required, max 512)
- `SizeBytes: long?` (only for `File`)
- `MimeType: string?` (optional)
- `LastModifiedUtc: DateTimeOffset?` (optional)
- `MetadataJson: string?` (optional extension payload)

### CatalogTag
- `Id: long` (PK)
- `CatalogId: Guid` (FK -> Catalog)
- `Name: string` (required, max 128)
- `Value: string` (required, max 1024)

## Relationship/Cardinality Table
| From | To | Cardinality | Notes |
|---|---|---|---|
| Catalog | CatalogNode | 1:N | Cascade delete |
| CatalogNode | CatalogNode (Parent->Children) | 1:N | Root nodes have `ParentId = null` |
| Catalog | CatalogTag | 1:N | Cascade delete |

## EF Core Mapping Notes (for #63)
- Use `DbContext` entity sets: `Catalogs`, `Nodes`, `Tags`.
- Configure `CatalogNode.Kind` as numeric enum storage.
- Add index on `(CatalogId, ParentId)` for hierarchy queries.
- Add index on `(CatalogId, Kind)` for quick file/folder filtering.
- Add unique constraint on `(CatalogId, ParentId, Name, Kind)` to reduce accidental duplicates.
- Configure `SizeBytes` nullable; enforce folder/file constraints in domain validation and tests.

## Versioning Strategy
- `SchemaVersion` is persisted per catalog.
- `1` is the baseline schema for v3 rollout.
- Any breaking data shape change increments schema version.
- Migrations must include both:
  - DB migration steps (EF migration in #63)
  - domain/DTO compatibility notes in docs

## Legacy Field Mapping (v2 -> v3)
| Legacy Field | v3 Target | Rule |
|---|---|---|
| `ID` | `CatalogNode.Id` | Preserve where possible during import |
| `Name` | `CatalogNode.Name` | Direct map |
| `ParentID` | `CatalogNode.ParentId` | Direct map, convert invalid parents to root + warning |
| `Type` | `CatalogNode.Kind` | `scdFile` -> `File`, all others -> `Folder` |
| `Properties` | `CatalogNode.MetadataJson` | Preserve serialized value |
| `Size` | `CatalogNode.SizeBytes` | For file nodes only; otherwise null |
| `AID` | `Catalog.Id` | Map imported collection scope to a single `Catalog` identity |

## Validation Rules
- Catalog name is required.
- `SchemaVersion > 0`.
- Node name is required.
- `Folder` nodes must have `SizeBytes = null`.
- `File` nodes cannot have negative `SizeBytes`.

## Plugin DTO Examples
### Catalog summary payload
```json
{
  "catalogId": "9c3bc0dc-b0dd-4e0e-8efd-97353d6d1f14",
  "name": "Archive 2026",
  "schemaVersion": 1,
  "createdUtc": "2026-04-18T12:00:00Z",
  "updatedUtc": "2026-04-18T12:00:00Z"
}
```

### Node payload
```json
{
  "id": 42,
  "catalogId": "9c3bc0dc-b0dd-4e0e-8efd-97353d6d1f14",
  "parentId": 7,
  "kind": "File",
  "name": "manual.pdf",
  "sizeBytes": 1048576,
  "mimeType": "application/pdf",
  "lastModifiedUtc": "2026-04-12T09:41:00Z",
  "metadataJson": "{\"sha256\":\"...\"}"
}
```
