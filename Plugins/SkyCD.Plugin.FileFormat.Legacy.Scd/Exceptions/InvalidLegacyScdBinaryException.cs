using System.IO;

namespace SkyCD.Plugin.FileFormat.Legacy.Scd.Exceptions;

internal sealed class InvalidLegacyScdBinaryException(string reason)
    : IOException($"Invalid legacy binary SCD catalog: {reason}.");
