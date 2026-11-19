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
change traversal. FTP (`ftp://`, port 21) and SFTP (`sftp://`, port 22) are registered transports;
addresses without a scheme default to FTP. Unsupported schemes fail before connecting with a
localized typed error. The login dialog adds SFTP-only fields (port, authentication method,
key file, passphrase and host-key confirmation) when the selected scheme is `sftp`.

Remote scans use the same bounded retry, cancellation and statistics infrastructure. The traversal
policy is supplied through `RemoteScanOptions.Traversal`; when omitted, `ScanTraversalOptions.Default`
is used. The parsed public URL omits credentials; providers supply normalized directory identities.

### Link / cycle policy

- FTP listings do not expose reparse points, so `LinkFollowPolicy` does not apply; a server-side
  symbolic link is reported as a file entry and is not descended into.
- SFTP listings do expose symbolic links. A symlink is resolved to its target before mapping:
  a symlinked directory is catalogued under its link name but carries the target's path as its
  `RemoteEntry.VisitedIdentity`, so the shared visited-identity logic treats it as a cycle
  candidate — a link back into an already visited directory is skipped instead of recursed,
  and every alias of the same target is traversed at most once. A dangling link stays a file
  entry and is never descended into.
- The **stable visited identity** of a remote directory is the path normalized by its provider
  (or the resolved target path for symlinked directories).
  FTP collapses `.`, `..`, duplicate and trailing separators.
- A directory whose identity was already visited is **skipped** as a cycle, never descended
  into, so a server that lists a parent back into its child cannot loop the scan.

### Disconnect and reconnect

- A dropped connection (`RemoteConnectionException`) is **transient** and is retried.
- The retry **re-establishes the session** and repeats the listing.
- Rejected credentials (`RemoteAuthenticationException`), an unsupported scheme
  (`RemoteSchemeNotSupportedException`), a vanished directory (`RemoteDirectoryNotFoundException`),
  a refused listing (`RemoteListingNotSupportedException`), an untrusted host key
  (`RemoteHostKeyException`) and a failed TLS protection (`RemoteTlsException`) are permanent
  and are never retried.

### Transport (#580)

The FTP provider uses [FluentFTP](https://www.nuget.org/packages/FluentFTP) (MIT):

- `FtpCatalogConnector` opens one `FluentFTP.FtpClient` per session with `RetryAttempts = 1` (the
  scan owns retries and reconnects), `DataConnectionType = AutoPassive`
  (EPSV, then PASV), `TimeConversion = UTC`, `ListingParser = Auto` and 15 s connect / 20 s
  transfer timeouts. One connector instance serves one scheme: `ftp://` or `ftps://` (#623).
- FluentFTP handles LIST parsing and reply handling; SIZE and MDTM preserve exact metadata.
- `FtpRemoteListingMapper` projects listings onto `RemoteEntry`: self/parent records and invalid
  names are dropped, directory sizes are zeroed, symbolic permissions and UTC timestamps are kept.
- FTP failures map to the neutral exceptions, retaining the existing localized FTP messages.
- `tests/SkyCD.App.Tests/ScriptedFtpServer.cs` is a loopback scripted FTP server used by
  `FtpCatalogTransportTests` to cover nested listings, MLSD-less servers, refused listings,
  rejected credentials, missing directories and interrupted connections over a real socket.

### FTPS transport (#623)

FTPS is the same provider with FluentFTP's TLS support; no additional package is used:

- `RemoteTlsMode` (`Off` / `Explicit` / `Implicit`) is part of `RemoteLocation`. The scheme picks
  the default — `ftps://` is implicit TLS on port 990 — and an optional `tls=off|explicit|implicit`
  query parameter overrides it. The query is split off the raw address before `System.Uri`
  parsing, because .NET folds `?` into the path of `ftp://` addresses. The login dialog exposes a
  TLS mode selector whose choice is carried in the address, like the port.
- `FtpConfig.EncryptionMode` maps the mode to FluentFTP `None` / `Explicit` (AUTH TLS) /
  `Implicit`; data channels are protected (PROT P) in both TLS modes. The PBSZ 0 / PROT P upgrade
  is issued explicitly after login so a rejected upgrade reports `RemoteTlsDataProtectionException`
  instead of an ambiguous command failure; there is no plaintext fallback.
- Certificates are validated against the system trust store and the target host name by default.
  Validation failures report `RemoteCertificateValidationException`; handshake failures report
  `RemoteTlsHandshakeException` and a refused AUTH TLS reports `RemoteTlsUpgradeException`.
  All four `RemoteTlsException` kinds are localized, fatal and never retried.
- `tests/SkyCD.App.Tests/LocalFtpServer.cs` serves TLS (self-signed loopback certificate) in
  implicit and explicit modes with knobs for rejected AUTH TLS and rejected PROT P.
  `FtpsCatalogTransportTests` covers implicit and explicit listings over protected channels,
  certificate rejection, rejected AUTH TLS and PROT P, plaintext servers, authentication failure,
  cancellation and interrupted connections.

### SFTP transport (#624)

The SFTP provider uses [SSH.NET](https://www.nuget.org/packages/SSH.NET) (MIT):

- `SftpCatalogConnector` opens one `Renci.SshNet.SftpClient` per session with 15 s handshake /
  20 s operation timeouts. Password authentication and private-key authentication (key file plus
  optional passphrase) are supported; secrets are never persisted in the catalog and never appear
  in string representations or progress messages.
- SSH.NET handles the SSH transport, the key exchange and the SFTP v3 protocol; `SftpRemoteListingMapper`
  projects listings onto `RemoteEntry` with the same name, size, permission and UTC timestamp
  rules as the FTP mapper, and resolves symlinked entries to their target path for the visited
  identity.
- **Host keys are verified against `SftpHostKeyStore`** (a per-user JSON file, default
  `%APPDATA%/SkyCD/sftp-known-hosts.json`). Trust is only created by explicit user confirmation
  from the login dialog's host-key panel; there is no implicit trust path. An unknown key or a
  changed fingerprint fails the connection with the typed `RemoteHostKeyException` (localized,
  carrying the target and the `SHA256:` fingerprint) until the user confirms or replaces the entry.
- SFTP failures map to the neutral exceptions (`RemoteAuthenticationException`,
  `RemoteConnectionException`, `RemoteDirectoryNotFoundException`,
  `RemoteListingNotSupportedException`); timeouts map to the transient
  `RemoteConnectionException`, cancellation to `OperationCanceledException`.
- `tests/SkyCD.App.Tests/ScriptedSftpServer.cs` is an in-process SSH/SFTP server (FxSsh, MIT)
  used by `SftpCatalogTransportTests` to cover nested listings, password and key authentication,
  auth failure, unknown and changed host keys, cancellation and interrupted connections.

### WebDAV transport (#625)

The WebDAV provider uses [WebDav.Client](https://www.nuget.org/packages/WebDav.Client) (MIT,
no dependencies on .NET Standard 2.0):

- `WebDavCatalogConnector` serves two schemes from one implementation: `webdav://` (HTTP, port
  80) and `webdavs://` (HTTPS, port 443). Plain `http://` and `https://` addresses are **not**
  accepted — they stay unclaimed schemes, so an ordinary web page can never be mistaken for a
  remote source and users opt into WebDAV explicitly.
- Listings use `PROPFIND` with `Depth: 1`; WebDav.Client parses the `207 Multi-Status` body and
  `WebDavRemoteListingMapper` resolves each `href` against the request URI (relative or absolute),
  percent-decodes the path, drops the collection's own record and the protocol self/parent
  records, and maps only direct members: trailing-slash hrefs and `resourcetype` collections
  become directories, `getcontentlength`/`getlastmodified` become size and UTC timestamp.
- Authentication is Basic or Digest through `HttpClientHandler` credentials (negotiated from the
  server's `WWW-Authenticate` challenge); anonymous access sends no `Authorization` header.
  System proxy defaults and automatic redirects apply; a redirect chain that exceeds the limit
  surfaces as a connection failure. Secrets never appear in messages or progress output.
- The provider is **read-only**: uploads, moves and deletes are out of scope.
- Failure mapping: `401` → `RemoteAuthenticationException`, `403` and malformed/missing `207`
  bodies → `RemoteListingNotSupportedException`, `404` → `RemoteDirectoryNotFoundException`,
  `5xx`, transport errors, timeouts and redirect loops → the transient
  `RemoteConnectionException`. A Depth 0 `PROPFIND` at connect time surfaces refused credentials
  and unreachable endpoints in the "connect" stage like the other providers, while a missing
  path is reported by the first listing.
- `tests/SkyCD.App.Tests/WebDavCatalogTransportTests.cs` drives the connector against a fake
  `HttpMessageHandler` (nested listings, auth failure, not-found, forbidden, malformed and
  missing multistatus, partial propstat failure, cancellation, HTTPS scheme). The Basic/Digest
  handshake itself belongs to `HttpClientHandler`; verifying it end to end needs a live WebDAV
  server (for example Apache `mod_dav` or a NAS share) and is a manual check: add such a source
  with and without credentials in the Add To List dialog and compare the catalog with the
  server's own listing.

### Remote skip versus fatal

| Condition | Disposition |
| --- | --- |
| Authentication rejected / unsupported scheme | **Fatal** (typed exception) |
| Host key unknown or changed | **Fatal** (`RemoteHostKeyException`) |
| TLS certificate rejected / handshake or upgrade refused | **Fatal** (`RemoteTlsException`) |
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
