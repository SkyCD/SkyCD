# Issue #1034: OS drag-and-drop onto the main window

Dragging a media folder, a disc drive root, a network share or a remote catalog URL from the
operating system file manager onto the SkyCD main window opens the existing **Add To List** dialog
prefilled with the dropped source. Dropping nothing supported does nothing and shows no dialog.

## Mapping

| Dropped payload | Dialog state |
| --- | --- |
| Folder / directory | `Folder` source, path in `SourceValue`, leaf name in `MediaName` |
| Available drive root (`E:\`) | `Media` source with that drive selected in `AvailableDrives` |
| UNC path or mounted share | `Folder` source with the share path in `SourceValue` |
| Absolute remote URL text (`ftp`, `sftp`, `smb`, `webdav(s)`, `http(s)`) | `Internet` source prefilled |
| Plain text, single files, empty selection | Ignored |

SkyCD reaches HTTP servers through WebDAV, so an `http(s)` URL is prefilled as a `webdav`/`webdavs`
endpoint. SMB URLs follow the Internet tab and are accepted only on Windows.

Several dropped items open one dialog. The first root is preselected, and the dialog lists every
dropped root so the user picks which one to scan instead of scanning them all silently.

## Platform notes

The window handlers use Avalonia's drag-and-drop abstraction (`DragDrop` attached events and
`DragEventArgs.DataTransfer.TryGetFiles()`), so the same code path serves:

- **Windows** — Explorer's `CF_HDROP` payload;
- **Linux** — X11 and Wayland `text/uri-list` drags from GNOME Files, KDE Dolphin and similar;
- **macOS** — Finder drags, which Avalonia exposes as file storage items with local paths.

Duplicate roots (for example a folder dropped twice, with and without a trailing separator) collapse
to one entry. Paths with spaces, unicode characters and read-only media are used verbatim.

## Behaviour while hovering

While a supported payload hovers the window, a bordered overlay with a "drop to add" prompt covers
the catalog area and the pointer shows the copy cursor. Leaving the window or completing the drop
removes the overlay. Payloads that do not map to a source keep the default cursor and never show the
overlay, so in-app drags are unaffected.

## Code map

- `src/SkyCD.Presentation/ViewModels/DropSourceResolver.cs` — payload classification.
- `src/SkyCD.Presentation/ViewModels/AddToListDialogViewModel.cs` — `ApplyDroppedSources` prefill and
  the dropped-root picker.
- `src/SkyCD.App/Views/MainWindow.axaml(.cs)` — window-level drop handlers and the overlay.
- `src/SkyCD.App/Views/AddToListWindow.axaml` — the picker shown for multiple dropped roots.
