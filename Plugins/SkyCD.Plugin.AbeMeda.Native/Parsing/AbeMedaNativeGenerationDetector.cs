using System;
using SkyCD.Plugin.AbeMeda.Native.Models;

namespace SkyCD.Plugin.AbeMeda.Native.Parsing;

/// <summary>
/// Maps the file suffix onto the family generation. The extended format arrived
/// with NeoFinder 7, so suffix alone is enough to avoid applying the classic
/// layout to a file that has not been verified.
/// </summary>
public static class AbeMedaNativeGenerationDetector
{
    public static AbeMedaNativeGeneration Detect(string? fileNameOrPath)
    {
        var name = fileNameOrPath?.Trim() ?? string.Empty;
        if (name.EndsWith(".abemeda7", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".neofinder7", StringComparison.OrdinalIgnoreCase))
        {
            return AbeMedaNativeGeneration.Extended;
        }

        return AbeMedaNativeGeneration.Classic;
    }
}
