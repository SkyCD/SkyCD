using System;

namespace SkyCD.Plugin.Metadata.Fonts.Exceptions;

/// <summary>
/// Raised when a source is not a readable sfnt font or font collection. The reason parameter names the
/// specific defect and never contains file contents.
/// </summary>
public sealed class FontFormatException : Exception
{
    private const string Template = "The font could not be read: {0}.";

    public FontFormatException(string reason)
        : base(string.Format(System.Globalization.CultureInfo.InvariantCulture, Template, reason))
    {
    }
}
