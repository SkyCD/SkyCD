using System;
using System.Collections.Generic;
using System.Text;
using SkyCD.Plugin.EncodingProfiles;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Reads typed values from an OLE property set stream such as <c>SummaryInformation</c>.
/// </summary>
/// <remarks>
/// Only the first property set section is read, because that is where the standard document summary lives.
/// The parser is deliberately lenient: a malformed section, table or value ends parsing without throwing, so
/// a damaged summary stream costs the document its optional metadata rather than failing the extraction.
/// Values of unsupported types are skipped; unreadable string encodings fall back to Windows-1252.
/// </remarks>
internal static class OfficePropertySet
{
    private const uint VtI2 = 0x0002;
    private const uint VtI4 = 0x0003;
    private const uint VtBstr = 0x0008;
    private const uint VtUi2 = 0x0012;
    private const uint VtUi4 = 0x0013;
    private const uint VtLpStr = 0x001E;
    private const uint VtLpWStr = 0x001F;
    private const uint VtFileTime = 0x0040;

    private const uint CodePagePropertyId = 1;
    private const int DefaultCodePage = 1252;
    private const int MaxProperties = 1024;

    public static IReadOnlyDictionary<uint, object> Parse(byte[] stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var values = new Dictionary<uint, object>();
        if (stream.Length < 48 || ReadU16(stream, 0) != 0xFFFE)
        {
            return values;
        }

        var setCount = ReadU32(stream, 24);
        if (setCount is 0 or > 2)
        {
            return values;
        }

        var headerLength = 28 + (20 * (int)setCount);
        if (stream.Length < headerLength)
        {
            return values;
        }

        var sectionOffset = ReadU32(stream, 44);
        if (sectionOffset > int.MaxValue || sectionOffset < headerLength)
        {
            return values;
        }

        var section = (int)sectionOffset;
        if (section + 8 > stream.Length)
        {
            return values;
        }

        var sectionSize = ReadU32(stream, section);
        var propertyCount = ReadU32(stream, section + 4);
        if (sectionSize < 8 || sectionSize > (uint)(stream.Length - section) || propertyCount > MaxProperties
            || 8 + ((long)propertyCount * 8) > sectionSize)
        {
            return values;
        }

        var sectionEnd = section + (int)sectionSize;
        var table = section + 8;
        var encoding = ResolveEncoding(ReadCodePage(stream, table, (int)propertyCount, section, sectionEnd));

        for (var index = 0; index < propertyCount; index++)
        {
            var entry = table + (index * 8);
            var propertyId = ReadU32(stream, entry);
            var valueOffset = ReadU32(stream, entry + 4);

            if (propertyId <= CodePagePropertyId || valueOffset < 8 || valueOffset > int.MaxValue)
            {
                continue;
            }

            var valuePosition = section + (int)valueOffset;
            if (valuePosition + 4 > sectionEnd)
            {
                continue;
            }

            var value = ReadValue(stream, valuePosition, sectionEnd, encoding);
            if (value is not null && !values.ContainsKey(propertyId))
            {
                values.Add(propertyId, value);
            }
        }

        return values;
    }

    private static int ReadCodePage(byte[] stream, int table, int propertyCount, int section, int sectionEnd)
    {
        for (var index = 0; index < propertyCount; index++)
        {
            var entry = table + (index * 8);
            if (ReadU32(stream, entry) != CodePagePropertyId)
            {
                continue;
            }

            var offset = ReadU32(stream, entry + 4);
            if (offset < 8 || offset > int.MaxValue)
            {
                return DefaultCodePage;
            }

            var position = section + (int)offset;
            if (position + 8 > sectionEnd)
            {
                return DefaultCodePage;
            }

            var type = ReadU32(stream, position) & 0xFFFF;
            return type switch
            {
                VtI2 => (ushort)ReadI16(stream, position + 4),
                VtUi2 => ReadU16(stream, position + 4),
                VtI4 or VtUi4 when position + 8 <= sectionEnd => (int)ReadU32(stream, position + 4),
                _ => DefaultCodePage
            };
        }

        return DefaultCodePage;
    }

    private static object? ReadValue(byte[] stream, int position, int sectionEnd, Encoding encoding)
    {
        var variantType = ReadU32(stream, position);
        if ((variantType & 0x1000) != 0)
        {
            // Vectors such as heading pairs or part titles are not part of the published schema.
            return null;
        }

        var type = variantType & 0xFFFF;
        var value = position + 4;
        switch (type)
        {
            case VtI2 when value + 2 <= sectionEnd:
                return (long)ReadI16(stream, value);
            case VtUi2 when value + 2 <= sectionEnd:
                return (long)ReadU16(stream, value);
            case VtI4 when value + 4 <= sectionEnd:
                return (long)ReadI32(stream, value);
            case VtUi4 when value + 4 <= sectionEnd:
                return (long)ReadU32(stream, value);
            case VtFileTime when value + 8 <= sectionEnd:
                return ReadFileTime(ReadU64(stream, value));
            case VtLpStr:
                return ReadString(stream, value, sectionEnd, encoding, wide: false, bstr: false);
            case VtLpWStr:
                return ReadString(stream, value, sectionEnd, Encoding.Unicode, wide: true, bstr: false);
            case VtBstr:
                return ReadString(stream, value, sectionEnd, Encoding.Unicode, wide: false, bstr: true);
            default:
                return null;
        }
    }

    private static string? ReadString(byte[] stream, int lengthPosition, int sectionEnd, Encoding encoding,
        bool wide, bool bstr)
    {
        if (lengthPosition + 4 > sectionEnd)
        {
            return null;
        }

        var length = ReadU32(stream, lengthPosition);
        var dataStart = lengthPosition + 4;
        var available = (long)sectionEnd - dataStart;
        if (length > int.MaxValue)
        {
            return null;
        }

        var byteCount = wide ? (long)length * 2 : length;
        if (byteCount > available)
        {
            return null;
        }

        var text = (wide || bstr ? Encoding.Unicode : encoding)
            .GetString(stream, dataStart, (int)byteCount);
        return Normalize(text);
    }

    private static DateTimeOffset? ReadFileTime(ulong raw)
    {
        if (raw == 0 || raw > long.MaxValue)
        {
            return null;
        }

        try
        {
            var utc = DateTime.FromFileTimeUtc((long)raw);
            return utc.Year is < 1601 or > 9999 ? null : new DateTimeOffset(utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var character in text)
        {
            if (character is '\0' || char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static Encoding ResolveEncoding(int codePage)
    {
        try
        {
            return LegacyEncoding.Get(codePage);
        }
        catch (ArgumentException)
        {
            return LegacyEncoding.Windows1252;
        }
        catch (NotSupportedException)
        {
            return LegacyEncoding.Windows1252;
        }
    }

    private static short ReadI16(byte[] data, int offset) => (short)ReadU16(data, offset);

    private static int ReadI32(byte[] data, int offset) => (int)ReadU32(data, offset);

    private static ushort ReadU16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadU32(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private static ulong ReadU64(byte[] data, int offset) =>
        ReadU32(data, offset) | ((ulong)ReadU32(data, offset + 4) << 32);
}
