using System.IO;

namespace SkyCD.Plugin.FileFormat.LegacySkyCD.Exceptions;

internal sealed class InvalidLegacyScdBinaryException(string reason)
    : IOException($"Invalid legacy binary SCD catalog: {reason}.");
