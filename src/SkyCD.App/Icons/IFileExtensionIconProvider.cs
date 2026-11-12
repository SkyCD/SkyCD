using Avalonia.Media.Imaging;

namespace SkyCD.App.Icons;

public interface IFileExtensionIconProvider
{
    Bitmap? ResolveExtensionIcon(string extension);
}
