using System;
using System.IO;

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
