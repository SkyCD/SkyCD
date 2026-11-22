# Disk Explorer Professional HTML report profile (`disk-explorer-html-report`)

Read/write capability in `SkyCD.Plugin.FileFormat.DiskExplorer` (same assembly as the XML
profile, plus an AngleSharp parsing dependency — MIT, parse-only, no script
execution). Discovery uses assembly metadata and the capability type; no
`plugin.json` is involved.

## Report bundles

A report is a root/index `.htm`/`.html` page plus a sibling directory with the
same base name holding one page per category/volume/folder, thumbnails and other
assets. Index pages link volumes and folders with relative links; subpages link
their children the same way. Files are inline on their parent page. Detection
requires report-shaped markup (canonical `dx-*` hooks or a Disk Explorer title
marker) so unrelated HTML files sharing the extension are never claimed.

## Canonical markup

```html
<!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 4.01 Transitional//EN">
<html><head>
<meta http-equiv="Content-Type" content="text/html; charset=utf-8">
<title>Home Archive</title>
</head><body>
<div class="dx-report" data-title="Home Archive">
<div class="dx-item" data-itemtype="Volume" data-name="V" data-size=".." data-date=".."
     data-time=".." data-type=".." data-files=".." data-folders="..">
<a class="dx-name" href="report/p0000.html">V</a>
<div class="dx-desc">…</div>
<img class="dx-thumb" src="report/thumbs/t0000.png" alt="">
<a class="dx-child" href="report/p0001.html">Docs</a>
</div>
</div></body></html>
```

Exports write exactly this shape (HTML 4.01 Transitional compatible, UTF-8 by
default with a Windows-1252 compatibility option). Imports also accept legacy
shapes: headings as names, definition lists and two-column tables as labeled
fields, and plain relative links as children. Unknown `data-*` fields are kept
as `DiskExplorerHtml.*` properties; thumbnails resolve to `HasThumbnail` plus a
bundle-relative `ThumbnailFile` when the asset exists within the bundle and the
size cap.

## Import rules

Pages are parsed with AngleSharp (no script execution, entities decoded);
everything else is defensive text handling. Hierarchy rebuilds from page links
with canonical-path deduplication and cycle detection. Missing pages and assets
produce a partial import with warnings, never fabricated nodes. Dates normalize
to ISO when they match known formats and are otherwise kept verbatim; sizes are
unsigned decimals (`0` when absent); unknown disk types and columns are retained
as extension metadata. Single pages without a bundle path import as partial
without following links.

## Safety bounds

Only relative links contained in the report root resolve: absolute, UNC,
drive-qualified and parent-escaping paths, remote URLs, special schemes
(`http:`, `javascript:`, `data:`, …) and reparse-point escapes are skipped with
counted warnings and never fetched. Bounded traversal: 10,000 pages, depth 64,
8 MiB per page, 50,000 assets, 8 MiB thumbnails, 32 KiB descriptions, with
cancellation honored throughout.

## Export

Requires a file target: the bundle (root page plus sibling directory with
subpages and copied thumbnails) is written next to it. Deterministic in row
encounter order with sequential page names; child counts are computed from the
rows. File rows stream through; page rendering buffers rows once. Missing
thumbnails and unknown encodings fail or skip with clear diagnostics instead of
partial files.

## Tests

`tests/SkyCD.Plugin.FileFormat.DiskExplorer.Tests/DiskExplorerHtmlTests.cs` covers complete
and legacy (Windows-1252) bundles, Unicode/entities, missing pages and assets,
cycles, malicious inputs (no fetch, all skipped), empty/wrong documents,
detection with position preservation, single-page partial import, exact export
round trips, escaping/encodings, non-file-target rejection, depth limits, path
safety and cancellation, over synthetic fixtures under `Fixtures/Html`.
