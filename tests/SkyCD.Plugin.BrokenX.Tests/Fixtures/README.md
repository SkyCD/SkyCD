# Broken X Database fixtures

No genuine `.bxd` files may be redistributed, so the test suite builds every
fixture at runtime from synthetic trees (`BxdFixture`) serialized by the plugin
writer itself. Malformed inputs are produced by byte-level mutation of valid
archives, which keeps the fixtures license-safe while exercising the exact
bounds the reader enforces.

## Canonical empty database

The 42-byte empty v`0x40` archive is asserted byte-for-byte against the format
specification (signature, version `0x40`, root properties `0x42=1`, `0x4E=1`,
`0x4F=0x04150953`, zero items). Any intentional change to the writer's canonical
output breaks `Write_EmptyDatabase_EmitsCanonicalBytes`.

## Fixture inventory

- One item, siblings with last-sibling flags at depth, three-level nesting.
- Unicode names (composed accented Latin, Cyrillic, Japanese).
- Unknown property descriptors of all three payload kinds, plus an unknown
  blob root property; values and ordering must round-trip verbatim.
- A 1 MiB blob property (4 GiB-scale values use the same uint32-length path
  and must not exhaust memory; see `BxdFormat.MaxBlobBytes`).
- A 70000-byte name exercising the `FF FF FF` + uint32 long string form.
- A record with the reserved protected bit set, rejected with
  `BxdEncryptedRecordException`.
- Malformed: bad signature, unsupported version `0x41`, truncation at several
  points, invalid `0x00` descriptor, lying item count, trailing garbage,
  unclosed folders, 70-level nesting (limit is 64).
- Merge: disjoint sources concatenate; same-media sources union; conflicting
  rows keep the first occurrence with a warning.
- Subtree export: selected roots plus descendants; unknown or empty selections
  are rejected with `BxdSelectionException`.

## Independent validator

`BxdStructuralValidator` reimplements the specification without touching plugin
code: signature, version, bounded strings/properties, descriptor validity,
UTF-8 strictness, exact record counts, trailing data, unclosed folders and the
depth cap. Writer output must validate cleanly; every malformed fixture must
produce at least one validator issue.
