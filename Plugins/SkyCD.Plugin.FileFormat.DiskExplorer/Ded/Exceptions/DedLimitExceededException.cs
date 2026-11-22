using System;
using System.Globalization;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Exceptions;

/// <summary>Raised when an untrusted length or count exceeds a configured limit.</summary>
public sealed class DedLimitExceededException(string limit, ulong requested)
    : InvalidOperationException(
        $"Disk Explorer database {limit} of {requested.ToString(CultureInfo.InvariantCulture)} exceeds the configured limit.");
