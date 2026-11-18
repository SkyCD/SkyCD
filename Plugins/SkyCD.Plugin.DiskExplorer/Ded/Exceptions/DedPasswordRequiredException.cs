using System;

namespace SkyCD.Plugin.DiskExplorer.Ded.Exceptions;

/// <summary>Raised when a password-protected DED database is opened without a password.</summary>
public sealed class DedPasswordRequiredException()
    : InvalidOperationException("Disk Explorer database is password protected; a password is required.");
