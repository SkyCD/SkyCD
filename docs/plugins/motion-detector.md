# Motion Detector

Build `Plugins/SkyCD.Plugin.Feature.MotionDetector` and install its assembly and the platform native
OpenCV library through the normal plugin loader: `OpenCvSharpExtern.dll` on Windows (the x64 build also
needs `opencv_videoio_ffmpeg4130_64.dll`), `libOpenCvSharpExtern.so` on Linux and
`libOpenCvSharpExtern.dylib` on macOS. The project copies the library that matches the build or publish
runtime (`$(RuntimeIdentifier)` when set, otherwise the SDK host runtime) into the plugin folder.
Discovery uses assembly capabilities; no `plugin.json` is required.

The capture engine runs on Windows x64/arm64, Linux x64/arm64 and macOS x64/arm64. A runtime library that
cannot load — for example on a Linux host without the GTK 3 stack OpenCV's highgui needs — degrades to
the inline "OpenCV runtime could not be loaded" message instead of failing plugin discovery.

## Capture source

The plugin contributes a capture source type to the **Add To List** dialog. The source button appears only
while at least one enabled plugin provides `ICaptureSourcePluginCapability`, and disappears again when the
plugin is disabled from Options without restarting the app. Plugin enable state is driven by the activation
hook: `IActivatablePluginCapability` starts every saved source on activation and stops it on deactivation,
and repeated discovery passes never start a second loop for one source. Sources pause while no catalog is
open (`ICatalogLifecyclePluginCapability`) and while the plugin is inactive.

The dialog panel collects, top to bottom:

- capture provider and source type: File, Local device, JPEG URL, MJPEG URL, MMS Stream;
- the location field that the kind needs (file picker, webcam picker or URL box);
- **Preview**, which opens a dialog showing one frame with the detector overlay;
- a physical destination folder, required and always absolute;
- an optional source label.

Multiple sources can be configured at once. The plugin owns their configuration: `AddSourceAsync` persists
each source through `IPluginStorageApi` in the open catalog, and the host keeps no motion-detector state.

Captured frames are written by the plugin into the physical destination folder and reported to the host
through `ICaptureSourceHostApi.ImportCapturedFilesAsync`. The host creates the catalog entries under
`<root>/<capture folder>/<yyyy-MM-dd>/`, recalculates ancestor sizes and refreshes the browser.

## Detectors

Options → **Motion detector** selects one detector for every source, using the scientific names:

| Detector | Parameters |
| --- | --- |
| Frame difference | threshold, minimum area |
| Running average | threshold, learning rate, minimum area |
| Mixture of Gaussians - MOG2 | history, variance threshold, learning rate, minimum area |
| Mixture of Gaussians - KNN | history, distance threshold, learning rate, minimum area |
| Optical flow - Farnebäck | magnitude threshold, minimum area |
| Edge change ratio | Canny low/high, change percent, minimum area |
| Perceptual difference | difference percent |

Detection runs in-process with OpenCvSharp (Apache-2.0). Changing the detector restarts running sources; the
change is stored next to the sources and applies to sources without an override.

## Metadata queue

Every imported capture is queued durably in the open catalog (`capture_metadata_queue`, one marker per entry
id). Extraction runs on a background task through the media pipeline, reports progress on the status bar,
and never blocks the dialog or shows a modal. A marker is de-duplicated by entry id, drains oldest first, and
survives a restart; a file that is still flushing is retried a bounded number of times before the marker is
dropped. Captures imported while the media services have no providers stay queued until extraction can run.

## Failure behaviour

Preview, device enumeration and stream opening degrade gracefully with an inline message in the dialog; a
missing destination, unreadable source or unavailable catalog is reported on the status bar without stopping
the other sources.
