using System.Globalization;
using System.IO;

namespace SkyCD.Plugin.Metadata.DiskImages.Ccd.Exceptions;

/// <summary>
/// Raised when the IMG data file named by a CCD descriptor is not present next to the descriptor.
/// </summary>
public sealed class CcdMissingImageException : IOException
{
    private const string Template = "The CCD companion image '{0}' was not found next to the descriptor.";

    /// <summary>
    /// Initializes a new instance naming the missing companion file.
    /// </summary>
    public CcdMissingImageException(string fileName)
        : base(string.Format(CultureInfo.InvariantCulture, Template, fileName))
    {
    }
}
