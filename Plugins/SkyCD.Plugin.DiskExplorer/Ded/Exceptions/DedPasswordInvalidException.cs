using System;

namespace SkyCD.Plugin.DiskExplorer.Ded.Exceptions;

/// <summary>
/// Raised when a password was supplied for a protected DED database. The
/// protection algorithm is not implemented, so no password can be validated and
/// plaintext is never retained or logged.
/// </summary>
public sealed class DedPasswordInvalidException()
    : InvalidOperationException(
        "Disk Explorer database password could not be accepted; password protection is not supported by this plugin.");
