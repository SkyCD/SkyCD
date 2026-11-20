using System.Globalization;
using System.IO;

namespace SkyCD.Plugin.BinCue.Exceptions;

/// <summary>
/// Raised when the BIN data file named by a CUE sheet is not present next to the sheet.
/// </summary>
public sealed class BinCueMissingImageException : IOException
{
    private const string Template = "The BIN/CUE companion image '{0}' was not found next to the CUE sheet.";

    /// <summary>
    /// Initializes a new instance naming the missing companion file.
    /// </summary>
    public BinCueMissingImageException(string fileName)
        : base(string.Format(CultureInfo.InvariantCulture, Template, fileName))
    {
    }
}
