using System.IO;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed class RemoteDirectoryNotFoundException(string path)
    : DirectoryNotFoundException(LocalizationService.Current.Format(TextKeys.RemoteDirectoryNotFound, path))
{
}
