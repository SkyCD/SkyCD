# SkyCD JSON Catalog (`skycd-json`)

The native JSON catalog is the lossless format for SkyCD 3.0. It stores every entry field and the
catalog-scope data that feature plugins contribute, so saving to it raises no data-loss warning.

## Envelope

```json
{
  "schemaVersion": "skycd.catalog.v1",
  "payload": [ { "id": "file-1", "parentId": "root-1", "name": "Track.mp3", "type": "File", "size": 12345, "status": "loaned-out", "properties": { "Path": "C:\\Music\\Track.mp3" } } ],
  "catalogScope": [ { "category": "loans", "name": "activeLoans", "affectedCount": 1, "sample": "Alice" } ]
}
```

- `schemaVersion` must be `skycd.catalog.v1`.
- `payload` holds the catalog rows exactly as the host snapshot produced them.
- `catalogScope` is optional and appears only when scope data is present: virtual collections, custom
  statuses, active loans, borrowers, custom scan exclusion rules, and custom AI scan exclusions.
  Feature plugins supply these groups through `ICatalogAuxiliaryDataProvider`; the host serializes them
  with the entry payload so the saved file records the same data the preflight assessment reported.

Other writable formats declare which entry fields and catalog-scope items they cannot represent. Saving
to them shows a confirmation dialog listing the losses; the native JSON format reports none. See
[sdk-contracts.md](sdk-contracts.md#catalog-data-loss-preflight-sdk-30).
