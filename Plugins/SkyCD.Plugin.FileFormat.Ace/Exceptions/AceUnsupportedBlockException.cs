using System.Globalization;
using System.IO;

namespace SkyCD.Plugin.FileFormat.Ace.Exceptions;

/// <summary>
/// Raised when an archive header uses a compression method, block type or layout this plugin does not
/// support. The archive is still structurally valid, so callers usually skip the entry instead of failing.
/// </summary>
public sealed class AceUnsupportedBlockException : IOException
{
    private const string Template = "ACE header '{0}' uses unsupported {1} value {2}.";

    /// <summary>
    /// Initializes a new instance naming the header, the unsupported field and its value.
    /// </summary>
    public AceUnsupportedBlockException(string name, string field, int value)
        : base(string.Format(CultureInfo.InvariantCulture, Template, name, field, value))
    {
    }
}
