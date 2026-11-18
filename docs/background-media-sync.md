# Background media synchronization

Enable **Tools → Automatically sync mounted media** to scan media already present
in the current catalog. It is off by default, and the preference survives restart.
A pass starts when enabled and repeats every five minutes while the application
is open. Turning it off, replacing the active view model or closing the application
cancels the worker. This is an application background service, not an operating
system daemon.

Only Media entries with saved drive metadata are eligible. The mounted drive must
be ready and match the saved volume label, format and total capacity at its saved
root path. These checks are also repeated after traversal. They are compatibility
checks, not a unique hardware identity; media with identical labels, formats and
capacities cannot be distinguished by the current drive abstraction.

Traversal runs outside the UI thread, with one pass at a time and a two-minute
timeout per medium. It does not follow directory links. Publication returns to
the owning UI context, skips busy catalog operations, and checks that the original
catalog root still exists. Offline, missing, ambiguous or failed media are skipped.
Failures are reported in the status bar and do not prevent later media from syncing.

The existing scan publisher merges new and changed entries while preserving stable
IDs, user comments, tags, status and custom properties. Publications enter shared
undo history; unchanged passes create no history entry. Changes are persisted in
the working catalog and mark it dirty for normal catalog-file saving.
Synchronization never writes source files or removes old catalog entries, so an
offline or partially readable source cannot erase its cataloged contents.
