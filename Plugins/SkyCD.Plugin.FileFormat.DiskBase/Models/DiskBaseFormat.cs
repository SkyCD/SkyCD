using System;
using System.Globalization;
using System.Text;
using SkyCD.Plugin.FileFormat.DiskBase.Exceptions;

namespace SkyCD.Plugin.FileFormat.DiskBase.Models;

internal static class DiskBaseFormat
{
    internal const int KeyOffset = 4937;
    internal const int DataOffset = KeyOffset + 24 + 10000 * 6;
    internal const int Version = 0x0501;
    internal const int TreeInfoSize = 1180;
    internal const int MaximumNodes = 1000000;
    internal const int MaximumDepth = 512;
    internal static readonly byte[] Signature = Encoding.ASCII.GetBytes("QuickDir Database ");

    internal static Encoding GetEncoding(int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    internal static string Decode(byte[] bytes, Encoding encoding)
    {
        try
        {
            return encoding.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new DiskBaseFormatException($"text cannot be decoded using code page {encoding.CodePage}.");
        }
    }

    internal static string? DosTimestamp(uint packed)
    {
        if (packed == 0)
        {
            return null;
        }

        var date = packed >> 16;
        var time = packed & 0xffff;
        try
        {
            var value = new DateTime(1980 + (int)(date >> 9), (int)((date >> 5) & 15), (int)(date & 31),
                (int)(time >> 11), (int)((time >> 5) & 63), (int)((time & 31) * 2), DateTimeKind.Unspecified);
            return value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new DiskBaseFormatException("a DOS timestamp is invalid.");
        }
    }
}
