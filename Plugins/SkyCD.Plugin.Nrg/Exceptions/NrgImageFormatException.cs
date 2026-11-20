using System.Globalization;
using System.IO;

namespace SkyCD.Plugin.Nrg.Exceptions;

/// <summary>
/// Raised when an NRG image is damaged, or does not contain a data track this plugin can read.
/// </summary>
public sealed class NrgImageFormatException : IOException
{
    private const string Template = "The NRG image cannot be read: {0}";

    /// <summary>
    /// Initializes a new instance with the reason appended to the fixed message template.
    /// </summary>
    public NrgImageFormatException(string reason)
        : base(string.Format(CultureInfo.InvariantCulture, Template, reason))
    {
    }
}
