using System;

namespace SkyCD.Plugin.FileFormat.Librer.Exceptions;

/// <summary>
/// Exception thrown when the Librer data format version is not supported.
/// </summary>
public sealed class LibrerUnsupportedVersionException : Exception
{
    private const string PredefinedMessage = "Unsupported Librer data format version: '{0}'.";

    public LibrerUnsupportedVersionException(string version)
        : base(string.Format(PredefinedMessage, version))
    {
    }

    public LibrerUnsupportedVersionException(string version, Exception innerException)
        : base(string.Format(PredefinedMessage, version), innerException)
    {
    }
}
