using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkyCD.SDK.Capabilities.Ui;

namespace SkyCD.App.PluginHost;

/// <summary>
/// Host file pickers backed by the owning window's storage provider. A headless host has no window and
/// returns null, which lets a plugin report that no picker is available.
/// </summary>
public sealed class AvaloniaHostFileDialogApi(Func<Window?> ownerProvider) : IHostFileDialogApi
{
    /// <inheritdoc />
    public Task<string?> ShowOpenAsync(OpenFileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ShowAsync(owner => owner is null
            ? Task.FromResult<string?>(null)
            : PickOpenAsync(owner, request));
    }

    /// <inheritdoc />
    public Task<string?> ShowSaveAsync(SaveFileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ShowAsync(owner => owner is null
            ? Task.FromResult<string?>(null)
            : PickSaveAsync(owner, request));
    }

    private Task<string?> ShowAsync(Func<Window?, Task<string?>> action)
    {
        var owner = ownerProvider();
        return owner is null
            ? action(null)
            : Dispatcher.UIThread.InvokeAsync(() => action(owner));
    }

    private static async Task<string?> PickOpenAsync(Window owner, OpenFileRequest request)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = request.Title,
            AllowMultiple = request.AllowMultiple,
            FileTypeFilter = request.Filters
                .Select(filter => new FilePickerFileType(filter.Name) { Patterns = filter.Patterns.ToArray() })
                .ToArray()
        }).ConfigureAwait(true);

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private static async Task<string?> PickSaveAsync(Window owner, SaveFileRequest request)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = request.Title,
            SuggestedFileName = request.SuggestedFileName,
            DefaultExtension = request.DefaultExtension?.TrimStart('.'),
            FileTypeChoices = request.Filters?
                .Select(filter => new FilePickerFileType(filter.Name) { Patterns = filter.Patterns.ToArray() })
                .ToArray()
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }
}
