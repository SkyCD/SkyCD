using System.Globalization;
using System.IO;

namespace SkyCD.Plugin.FileFormat.Mdf.Exceptions;

/// <summary>
/// Raised when the data file that belongs to an MDS descriptor is not present next to the descriptor.
/// </summary>
public sealed class MdfMissingImageException : IOException
{
    private const string Template = "The MDF companion image '{0}' was not found next to the descriptor.";

    /// <summary>
    /// Initializes a new instance naming the missing companion file.
    /// </summary>
    public MdfMissingImageException(string fileName)
        : base(string.Format(CultureInfo.InvariantCulture, Template, fileName))
    {
    }
}
