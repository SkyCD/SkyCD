# Catalog scan traversal policy

Shared folder, media and remote catalog scanning uses a resilient traversal pipeline (`FileSystemCatalogTraverser` for local media, `RemoteCatalogScanner` for remote catalogs) with explicit skip-versus-fatal behavior, bounded retries, and structured operation events (#420).

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

## Remote provider and traversal policy

`RemoteCatalogScanner` selects an `IRemoteCatalogConnector` from `RemoteCatalogProviderRegistry`
by URL scheme. A provider supplies its default port, path and credential rules, connection error
messages, and `IRemoteCatalogSession` directory listings. Registering another provider does not
change traversal or the login flow. FTP is the only registered transport; addresses without a scheme
default to FTP. Unsupported schemes fail before connecting with a localized typed error.

Remote scans use the same bounded retry, cancellation and statistics infrastructure. The traversal
policy is supplied through `RemoteScanOptions.Traversal`; when omitted, `ScanTraversalOptions.Default`
is used. The parsed public URL omits credentials; providers supply normalized directory identities.

### Link / cycle policy

- FTP listings do not expose reparse points, so `LinkFollowPolicy` does not apply; a server-side
  symbolic link is reported as a file entry and is not descended into.
- The **stable visited identity** of a remote directory is the path normalized by its provider.
  FTP collapses `.`, `..`, duplicate and trailing separators.
- A directory whose identity was already visited is **skipped** as a cycle, never descended
  into, so a server that lists a parent back into its child cannot loop the scan.

### Disconnect and reconnect

- A dropped connection (`RemoteConnectionException`) is **transient** and is retried.
- The retry **re-establishes the session** and repeats the listing.
- Rejected credentials (`RemoteAuthenticationException`), an unsupported scheme
  (`RemoteSchemeNotSupportedException`), a vanished directory (`RemoteDirectoryNotFoundException`)
  and a refused listing (`RemoteListingNotSupportedException`) are permanent and are never retried.

### Transport (#580)

The FTP provider uses [FluentFTP](https://www.nuget.org/packages/FluentFTP) (MIT):

- `FtpCatalogConnector` opens one `FluentFTP.FtpClient` per session with `RetryAttempts = 1` (the
  scan owns retries and reconnects), `EncryptionMode = None`, `DataConnectionType = AutoPassive`
  (EPSV, then PASV), `TimeConversion = UTC`, `ListingParser = Auto` and 15 s connect / 20 s
  transfer timeouts.
- FluentFTP handles LIST parsing and reply handling; SIZE and MDTM preserve exact metadata.
- `FtpRemoteListingMapper` projects listings onto `RemoteEntry`: self/parent records and invalid
  names are dropped, directory sizes are zeroed, symbolic permissions and UTC timestamps are kept.
- FTP failures map to the neutral exceptions, retaining the existing localized FTP messages.
- `tests/SkyCD.App.Tests/ScriptedFtpServer.cs` is a loopback scripted FTP server used by
  `FtpCatalogTransportTests` to cover nested listings, MLSD-less servers, refused listings,
  rejected credentials, missing directories and interrupted connections over a real socket.

### Remote skip versus fatal

| Condition | Disposition |
| --- | --- |
| Authentication rejected / unsupported scheme | **Fatal** (typed exception) |
| Root connect retry exhausted | **Fatal** (`RemoteConnectionException`) |
| Root listing retry exhausted | **Fatal** (`RemoteConnectionException`) |
| Cancellation or operation timeout | **Fatal** (`OperationCanceledException`) |
| Remote directory cycle | **Skip** |
| Child listing retry exhausted | **Fail item** (continue) |
| Child directory vanished | **Fail item** (continue) |
| Root listing refused by the server | **Fatal** (`RemoteListingNotSupportedException`) |
| Child listing refused by the server | **Fail item** (continue) |
| Entry excluded by pattern | **Skip** |

FTP listings carry no hidden/system attributes, so `HiddenSystemFilePolicy` only applies to
local folder and media scans. `ExclusionPatterns` apply to remote entry names as well and are
matched case-sensitively, because remote path casing is server-defined.

## Cancellation and publication

Folder, media and remote add-to-list dialogs expose a keyboard-accessible, localized Cancel action.
Closing an active scan requests cancellation and keeps the dialog open until the worker exits. The
same token reaches traversal, retries and metadata extraction. Cancellation during the preparation
step also prevents publication and reports a canceled outcome rather than success or an error.
If traversal reports failed child items, the dialogs discard the captured scan and report the failed
count; they preserve the previous catalog. Intentional exclusions and cycle skips do not prevent publication.

Scanners capture results outside the visible catalog. Publication writes every chunk inside one
Couchbase Lite transaction; `BatchSize` controls iteration chunks, not commit boundaries. A failure
rolls back all inserted and updated entries, including earlier chunks. `ScanPublishOptions.CancellationToken`
is checked before publication, between documents and before transaction completion. Cancellation
before commit rolls back; once commit succeeds the published scan remains successful. Undo is recorded
only after commit, as one operation subject to the existing history budget.

The UI publishes on its UI thread and disables cancellation once publication finishes. Large scans
still materialize their documents in memory and hold one database transaction during publication;
chunk size does not cap memory usage. Native synchronous filesystem calls and noncooperative plugin
calls cannot be interrupted until they return. Existing catalog schemas and local scanner signatures
are unchanged. Explicit partial publication through `CancelSession`/`FailSession` marks retained entries
incomplete and stores an error summary; ordinary dialog cancellation discards captured results.
