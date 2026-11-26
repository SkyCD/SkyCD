using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Capture;
using SkyCD.SDK.Capabilities.Menu;

namespace SkyCD.Presentation.Capture;

/// <summary>
/// Routes a capture-source plugin's frames into the live main window. The view model is resolved on each
/// call because the host window can be rebuilt while a capture source keeps running.
/// </summary>
public sealed class CaptureSourceHostApi(Func<MainWindowViewModel> resolveViewModel) : ICaptureSourceHostApi
{
    /// <inheritdoc />
    public bool IsCatalogOpen => resolveViewModel().IsCaptureCatalogOpen;

    /// <inheritdoc />
    public Task ImportCapturedFilesAsync(IReadOnlyList<CapturedCatalogFile> files,
        CancellationToken cancellationToken = default) =>
        resolveViewModel().ImportCapturedFilesAsync(files, cancellationToken);

    /// <inheritdoc />
    public Task NotifyAsync(string message, CancellationToken cancellationToken = default) =>
        resolveViewModel().ReportCaptureMessageAsync(message, cancellationToken);
}
