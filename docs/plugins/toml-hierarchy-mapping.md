# TOML Hierarchy Mapping (v1)

The TOML plugin (`skycd.plugin.sample.toml`) uses an adjacency-list model:

- `schema.version` must be `skycd.catalog.v1`
- `schema.hierarchy` is fixed to `adjacency-list`
- each `[[nodes]]` entry maps one catalog node with:
  - `nodeId`
  - `parentId` (empty string for root)
  - `kind` (`folder` or `file`)
  - `name`
  - `sizeBytes`

Writer output is deterministic by sorting rows on `nodeId` using ordinal string order before serialization.
