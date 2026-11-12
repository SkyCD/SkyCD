# Catalog scan traversal policy

Shared folder and media catalog scanning uses a resilient traversal pipeline (`FileSystemCatalogTraverser`) with explicit skip-versus-fatal behavior, bounded retries, and structured operation events (#420).

## Default link-follow policy

**Default: `LinkFollowPolicy.FollowDirectoryLinksOnce`.**

- Directory symbolic links and reparse points are followed at most once per **stable visited identity**.
- Visited identity is the normalized full path of the resolved final link target when available; otherwise the normalized entry path.
- If the same identity is encountered again, the entry is **skipped** as a cycle (not treated as a fatal scan failure).
- File links are cataloged as files and are never traversed as directories.
- `LinkFollowPolicy.NeverFollowLinks` lists directory link entries only when not otherwise excluded, and does not descend through them.

## Hidden / system / exclusion policy

- Default: `HiddenSystemFilePolicy.Include` (compatible with prior scans).
- Optional: `SkipHidden`, `SkipSystem`, or `SkipHiddenAndSystem`.
- `ExclusionPatterns` are simple wildcards matched against entry names (for example `*.tmp`, `Thumbs.db`).

## Skip versus fatal

| Condition | Disposition |
| --- | --- |
| Root missing / media not ready / root inaccessible | **Fatal** (typed exception) |
| Retry exhaustion while enumerating the root | **Fatal** (`ScanRetryExhaustedException`) |
| Cancellation or operation timeout | **Fatal** (`OperationCanceledException`) |
| Directory link cycle | **Skip** |
| Unreadable / permission denied child | **Fail item** (continue) |
| Disappearing child / long or invalid path | **Skip or fail item** (continue) |
| Transient disconnect on a non-root child after retries | **Fail item** (continue) |
| Hidden/system/exclusion match | **Skip** |

Failed and skipped items are reported through `IOperationProgress.ReportFailed` / `ReportSkipped` without credentials (`SecretSanitizer`).

## Retries, timeouts, concurrency

- `MaxRetries` (default 3) and `RetryDelay` bound transient I/O retries.
- Retry waits honor the scan `CancellationToken` (and optional `OperationTimeout`).
- `MaxConcurrency` is clamped to `1..ScanTraversalOptions.AbsoluteMaxConcurrency` (8).
