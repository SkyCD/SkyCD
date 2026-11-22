using System.Globalization;
using System.IO;

namespace SkyCD.Plugin.Metadata.Archives.Ace.Exceptions;

/// <summary>
/// Raised when an archive or one of its entries is encrypted and therefore cannot be listed or extracted.
/// </summary>
public sealed class AceEncryptedArchiveException : IOException
{
    private const string Template = "ACE entry '{0}' is encrypted.";

    /// <summary>
    /// Initializes a new instance naming the encrypted entry, or the archive itself when it has no entries.
    /// </summary>
    public AceEncryptedArchiveException(string name)
        : base(string.Format(CultureInfo.InvariantCulture, Template, name))
    {
    }
}
