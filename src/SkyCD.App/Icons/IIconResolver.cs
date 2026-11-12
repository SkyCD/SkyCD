using Avalonia.Media.Imaging;

namespace SkyCD.App.Icons;

public interface IIconResolver
{
    Bitmap? ResolveIcon(string key);
}
