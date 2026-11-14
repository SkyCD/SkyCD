# Catalog scan traversal policy

Shared folder, media and FTP catalog scanning uses a resilient traversal pipeline (`FileSystemCatalogTraverser` for local media, `FtpCatalogScanner` for remote catalogs) with explicit skip-versus-fatal behavior, bounded retries, and structured operation events (#420).

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

## FTP traversal policy

The FTP catalog scan (`FtpCatalogScanner`) applies the same bounded retry, cancellation and
statistics infrastructure to remote listings. The traversal policy is supplied through
`FtpScanOptions.Traversal`; when it is omitted `ScanTraversalOptions.Default` is used.

### Link / cycle policy

- FTP listings do not expose symlinks or reparse points, so `LinkFollowPolicy` does not apply.
- The **stable visited identity** of a remote directory is its normalized path
  (`FtpLocation.NormalizePath`), which collapses `.`, `..`, duplicate and trailing separators.
- A directory whose identity was already visited is **skipped** as a cycle, never descended
  into, so a server that lists a parent back into its child cannot loop the scan.

### Disconnect and reconnect

- A dropped control connection (`FtpConnectionException`) is **transient** and is retried.
- The retry **re-establishes the control session** and repeats the listing, so a server that
  drops the connection mid-traversal is survived instead of failing the scan.
- Rejected credentials (`FtpAuthenticationException`), an unsupported scheme
  (`FtpSchemeNotSupportedException`) and a vanished directory (`FtpDirectoryNotFoundException`)
  are permanent and are never retried.

### FTP skip versus fatal

| Condition | Disposition |
| --- | --- |
| Authentication rejected / unsupported scheme | **Fatal** (typed exception) |
| Root connect retry exhausted | **Fatal** (`FtpConnectionException`) |
| Root listing retry exhausted | **Fatal** (`FtpConnectionException`) |
| Cancellation or operation timeout | **Fatal** (`OperationCanceledException`) |
| Remote directory cycle | **Skip** |
| Child listing retry exhausted | **Fail item** (continue) |
| Child directory vanished | **Fail item** (continue) |
| Entry excluded by pattern | **Skip** |

FTP listings carry no hidden/system attributes, so `HiddenSystemFilePolicy` only applies to
local folder and media scans. `ExclusionPatterns` apply to remote entry names as well and are
matched case-sensitively, because remote path casing is server-defined.
