using System;
using System.IO;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed class FtpSchemeNotSupportedException : NotSupportedException
{
    public FtpSchemeNotSupportedException(string message)
        : base(message)
    {
    }
}

public sealed class FtpAuthenticationException : IOException
{
    public FtpAuthenticationException(string message)
        : base(message)
    {
    }
}

public sealed class FtpConnectionException : IOException
{
    public FtpConnectionException(string message)
        : base(message)
    {
    }

    public FtpConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class FtpDirectoryNotFoundException : DirectoryNotFoundException
{
    public FtpDirectoryNotFoundException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A remote directory could not be listed because the server answered with an unsupported or
/// refused command instead of a listing (no MLSD support and a refused LIST). The scan falls back
/// to the classic LIST dialect before it reports this, so reaching it means the listing itself
/// failed and the item cannot be read.
/// </summary>
public sealed class FtpListingNotSupportedException : IOException
{
    public FtpListingNotSupportedException(string path, string stage)
        : base(LocalizationService.Current.Format(TextKeys.FtpListingNotSupported, path))
    {
        Path = path;
        Stage = stage;
    }

    public string Path { get; }

    public string Stage { get; }
}
