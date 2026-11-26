using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Ui;

/// <summary>One file-type filter offered by a host file picker.</summary>
/// <param name="Name">Localized filter label.</param>
/// <param name="Patterns">Glob patterns such as <c>*.nfo</c>.</param>
public sealed record FileDialogFilter(string Name, IReadOnlyList<string> Patterns);

/// <summary>A request to let the user choose an existing file.</summary>
public sealed record OpenFileRequest(
    string Title,
    IReadOnlyList<FileDialogFilter> Filters,
    bool AllowMultiple = false);

/// <summary>A request to let the user choose where a file is written.</summary>
public sealed record SaveFileRequest(
    string Title,
    string SuggestedFileName,
    string? DefaultExtension = null,
    IReadOnlyList<FileDialogFilter>? Filters = null);

/// <summary>
/// Host-provided file pickers. A headless host returns null so a plugin can report that no picker exists
/// instead of constructing a window itself.
/// </summary>
public interface IHostFileDialogApi
{
    /// <summary>Shows an open-file picker and returns the chosen local path, or null when cancelled.</summary>
    Task<string?> ShowOpenAsync(OpenFileRequest request);

    /// <summary>Shows a save-file picker and returns the chosen local path, or null when cancelled.</summary>
    Task<string?> ShowSaveAsync(SaveFileRequest request);
}

/// <summary>Optional capability hook the host calls with its file picker API.</summary>
public interface IHostFileDialogConsumer : IPluginCapability
{
    /// <summary>Receives the host file picker API for the host process lifetime.</summary>
    void InitializeFileDialogApi(IHostFileDialogApi fileDialogApi);
}
