using System.IO;

namespace SkyCD.Presentation.Scanning;

public sealed class DirectoryToScanNotFoundException : DirectoryNotFoundException
{
    public DirectoryToScanNotFoundException(string path)
        : base($"The specified folder to scan does not exist: '{path}'.")
    {
    }
}
