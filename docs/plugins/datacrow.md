# Data Crow collection import (planned)

> Status: investigation only ([Issue #459](../implementation/issue-459-datacrow.md)).
> No `datacrow` plugin exists yet. This page records the planned route so a
> future implementation starts from evidence, not assumptions.

## Chosen route

Import Data Crow **XML item exports** (hierarchical, relationship- and
image-capable, module-scoped), with **CSV exports** as a documented-loss
fallback. Native HSQLDB databases are out of scope (no .NET engine, no
external schema contract). Data Crow source code must not be reused
(GPLv3, blocked by policy); the implementation will be clean-room from the
public documentation and user-supplied samples.

## What the format gives us (per official docs)

- One export per module (Movies, Music, Books, Images, Software/Games, TV
  series, plus custom XML-defined modules).
- Text/CSV: flat rows, user-chosen separator, optional header, images as
  `Main picture` + `Other pictures` (`|`-separated, order-preserving);
  relationships and newlines do not survive.
- Import-side conventions worth mirroring: `|`/semicolon/newline/comma
  multi-value lists, locale-tolerant numbers and dates, case-insensitive
  column matching.
- Images: copied sidecar folder preferred; otherwise original-location
  links resolved against the export folder and subfolders.

## What is still unknown (blocking implementation)

- Exact XML element/attribute names and the module identity marker.
- Whether loan/ownership fields appear in exports.
- Multi-value and custom-field encodings in the XML form.

These require real export fixtures captured from a running Data Crow (see
the issue doc for the capture checklist). Do not implement from guesses.
