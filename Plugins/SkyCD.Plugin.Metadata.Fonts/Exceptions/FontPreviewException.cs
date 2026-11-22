using System;

namespace SkyCD.Plugin.Metadata.Fonts.Exceptions;

/// <summary>
/// Raised when a preview cannot be rendered, for example because the glyphs cannot be decoded or the
/// configured bounds cannot be met.
/// </summary>
public sealed class FontPreviewException : Exception
{
    private const string Template = "The font preview could not be rendered: {0}.";

    public FontPreviewException(string reason)
        : base(string.Format(System.Globalization.CultureInfo.InvariantCulture, Template, reason))
    {
    }
}
