using System.IO;

namespace SkyCD.Plugin.Legacy.Scd;

internal sealed class InvalidLegacyScdBinaryException(string reason)
    : IOException($"Invalid legacy binary SCD catalog: {reason}.");
