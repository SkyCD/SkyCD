using System.Globalization;
using System.IO;

namespace SkyCD.Plugin.FileFormat.BinCue.Exceptions;

/// <summary>
/// Raised when a CUE sheet is damaged or does not describe a data track this plugin can read.
/// </summary>
public sealed class BinCueImageFormatException : IOException
{
    private const string Template = "The BIN/CUE image cannot be read: {0}";

    /// <summary>
    /// Initializes a new instance with the reason appended to the fixed message template.
    /// </summary>
    public BinCueImageFormatException(string reason)
        : base(string.Format(CultureInfo.InvariantCulture, Template, reason))
    {
    }
}
