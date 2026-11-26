using System;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

/// <summary>
/// Decodes the packed After Effects version word stored in the RIFX <c>head</c> chunk (bytes 4 to 7) and in
/// the <c>svap</c> chunk. Bit 31 first: bits 25-24 and 21-19 form the major version, 18-15 the minor version,
/// 14-11 the bug fix, 10-8 a stage and 7-0 the build; <c>0F 11 0E 02</c> is 26.2.1x2.
/// </summary>
internal static class AeVersion
{
    public static string? DecodeHead(ReadOnlySpan<byte> head)
    {
        if (head.Length < 8)
        {
            return null;
        }

        return DecodeWord(head.Slice(4, 4));
    }

    public static string? DecodeWord(ReadOnlySpan<byte> word)
    {
        if (word.Length < 4)
        {
            return null;
        }

        var packed = ((uint)word[0] << 24) | ((uint)word[1] << 16) | ((uint)word[2] << 8) | word[3];
        var major = (((packed >> 24) & 0x3) << 3) | ((packed >> 19) & 0x7);
        var minor = (packed >> 15) & 0xF;
        var bugFix = (packed >> 11) & 0xF;

        if (major is 0 or > 99)
        {
            return null;
        }

        return bugFix > 0 ? $"{major}.{minor}.{bugFix}" : $"{major}.{minor}";
    }
}
