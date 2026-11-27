using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Sources;

namespace SkyCD.Presentation.Sources;

/// <summary>
/// Routes a source-factory plugin's frames into the live main window. The view model is resolved on each
/// call because the host window can be rebuilt while a source keeps running.
/// </summary>
public sealed class SourceFactoryHostApi(Func<MainWindowViewModel> resolveViewModel) : ISourceFactoryHostApi
{
    /// <inheritdoc />
    public bool IsCatalogOpen => resolveViewModel().IsCaptureCatalogOpen;

    /// <inheritdoc />
    public Task ImportSourceFilesAsync(IReadOnlyList<SourceFile> files,
        CancellationToken cancellationToken = default) =>
        resolveViewModel().ImportSourceFilesAsync(files, cancellationToken);

    /// <inheritdoc />
    public Task NotifyAsync(string message, CancellationToken cancellationToken = default) =>
        resolveViewModel().ReportCaptureMessageAsync(message, cancellationToken);
}
