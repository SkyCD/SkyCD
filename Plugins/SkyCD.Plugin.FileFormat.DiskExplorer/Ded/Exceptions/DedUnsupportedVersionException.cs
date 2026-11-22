using System;
using System.Globalization;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Exceptions;

/// <summary>Raised when a DED container carries an unimplemented format version.</summary>
public sealed class DedUnsupportedVersionException(uint version)
    : InvalidOperationException(
        $"Disk Explorer database version 0x{version.ToString("X8", CultureInfo.InvariantCulture)} is not supported.");
