using System.Globalization;
using System.IO;

namespace SkyCD.Plugin.Metadata.Mdf.Exceptions;

/// <summary>
/// Raised when an MDF/MDS image is damaged or does not describe a filesystem this plugin can read.
/// </summary>
public sealed class MdfImageFormatException : IOException
{
    private const string Template = "The MDF image cannot be read: {0}";

    /// <summary>
    /// Initializes a new instance with the reason appended to the fixed message template.
    /// </summary>
    public MdfImageFormatException(string reason)
        : base(string.Format(CultureInfo.InvariantCulture, Template, reason))
    {
    }
}
