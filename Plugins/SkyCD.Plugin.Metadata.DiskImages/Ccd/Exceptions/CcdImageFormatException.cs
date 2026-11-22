using System.Globalization;
using System.IO;

namespace SkyCD.Plugin.Metadata.DiskImages.Ccd.Exceptions;

/// <summary>
/// Raised when a CCD descriptor is damaged or does not describe a data track this plugin can read.
/// </summary>
public sealed class CcdImageFormatException : IOException
{
    private const string Template = "The CCD image cannot be read: {0}";

    /// <summary>
    /// Initializes a new instance with the reason appended to the fixed message template.
    /// </summary>
    public CcdImageFormatException(string reason)
        : base(string.Format(CultureInfo.InvariantCulture, Template, reason))
    {
    }
}
