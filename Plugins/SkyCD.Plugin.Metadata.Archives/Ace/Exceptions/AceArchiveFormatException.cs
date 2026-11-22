using System.Globalization;
using System.IO;

namespace SkyCD.Plugin.Metadata.Archives.Ace.Exceptions;

/// <summary>
/// Raised when an archive signature, header or block structure is damaged or uses a layout this plugin
/// cannot read.
/// </summary>
public sealed class AceArchiveFormatException : IOException
{
    private const string Template = "The ACE archive cannot be read: {0}";

    /// <summary>
    /// Initializes a new instance with the reason appended to the fixed message template.
    /// </summary>
    public AceArchiveFormatException(string reason)
        : base(string.Format(CultureInfo.InvariantCulture, Template, reason))
    {
    }
}
