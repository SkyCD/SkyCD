using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SkyCD.Plugin.Feature.GenDesc.Views;

/// <summary>One file type offered by a plugin picker: display name plus a single extension pattern.</summary>
public sealed record GenDescFileType(string Name, string Pattern);

/// <summary>
/// Picks template and .nfo paths through the plugin's own Avalonia surface instead of a host contract.
/// A null picker means the requesting control has no top level yet, so nothing can be shown.
/// </summary>
public interface IGenDescFilePicker
{
    Task<string?> PickOpenAsync(string title, GenDescFileType fileType);

    Task<string?> PickSaveAsync(string title, string suggestedFileName, GenDescFileType fileType);
}

internal sealed class AvaloniaGenDescFilePicker(TopLevel topLevel) : IGenDescFilePicker
{
    public async Task<string?> PickOpenAsync(string title, GenDescFileType fileType)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [ToPickerType(fileType)]
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveAsync(string title, string suggestedFileName, GenDescFileType fileType)
    {
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = fileType.Pattern.TrimStart('*').TrimStart('.'),
            FileTypeChoices = [ToPickerType(fileType)]
        });

        return file?.TryGetLocalPath();
    }

    private static FilePickerFileType ToPickerType(GenDescFileType fileType) =>
        new(fileType.Name) { Patterns = [fileType.Pattern] };
}
