using System;

namespace SkyCD.Formatting.Rtf;

[Flags]
public enum RichFormatFlags : byte
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikeout = 8
}
