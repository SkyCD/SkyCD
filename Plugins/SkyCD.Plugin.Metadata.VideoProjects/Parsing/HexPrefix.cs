using System;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

/// <summary>Decodes the leading bytes of an <c>.aepx</c> <c>bdata</c> attribute without materialising the rest.</summary>
internal static class HexPrefix
{
    public static byte[] Decode(string? hex, int maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        if (string.IsNullOrEmpty(hex))
        {
            return [];
        }

        var available = Math.Min(maxBytes, hex.Length / 2);
        if (available == 0)
        {
            return [];
        }

        var bytes = new byte[available];
        for (var index = 0; index < available; index++)
        {
            var high = HexValue(hex[index * 2]);
            var low = HexValue(hex[(index * 2) + 1]);
            if (high < 0 || low < 0)
            {
                return bytes.AsSpan(0, index).ToArray();
            }

            bytes[index] = (byte)((high << 4) | low);
        }

        return bytes;
    }

    private static int HexValue(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'a' and <= 'f' => character - 'a' + 10,
        >= 'A' and <= 'F' => character - 'A' + 10,
        _ => -1
    };
}
