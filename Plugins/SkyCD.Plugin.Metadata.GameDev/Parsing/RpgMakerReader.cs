using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads RPG Maker MV/MZ project markers and XP/VX/VX Ace Ruby Marshal data. The Marshal walker counts
/// symbols, strings and container objects within fixed byte and depth bounds and never reconstructs the
/// object graph or resolves classes.
/// </summary>
internal static partial class RpgMakerReader
{
    internal static GameDevFacts? ReadProjectText(string text)
    {
        var match = ProjectMarker().Match(text.Length > 256 ? text[..256] : text);
        if (!match.Success)
        {
            return null;
        }

        var format = match.Groups["Family"].Value;
        return new GameDevFacts
        {
            Family = "RPG Maker",
            Format = format,
            Application = "RPG Maker",
            FormatVersion = match.Groups["Version"].Success ? match.Groups["Version"].Value : null,
            EngineVersion = match.Groups["Version"].Success ? match.Groups["Version"].Value : null
        };
    }

    internal static GameDevFacts? ReadMarshal(byte[] data, bool truncated)
    {
        if (data.Length < 2 || data[0] != 0x04 || data[1] != 0x08)
        {
            return new GameDevFacts
            {
                Family = "RPG Maker",
                Format = "Ruby Marshal",
                Application = "RPG Maker",
                Valid = false
            };
        }

        var walker = new MarshalWalker(data);
        walker.Run();
        var facts = new GameDevFacts
        {
            Family = "RPG Maker",
            Format = "Ruby Marshal",
            Application = "RPG Maker",
            Compiled = true,
            Valid = true,
            ScanIncomplete = truncated || walker.Stopped,
            BlockCount = walker.SymbolCount,
            ObjectCount = walker.ObjectCount,
            ResourceCount = walker.StringCount
        };

        foreach (var value in walker.Strings)
        {
            facts.AddNameSample(value);
        }

        return facts;
    }

    [GeneratedRegex(@"RPG(?<Family>MV|MZ)\s*(?<Version>\d+(?:\.\d+)*)?", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectMarker();

    /// <summary>Bounded opcode walker: consumes the stream, counting what it models, and stops on the unknown.</summary>
    private sealed class MarshalWalker
    {
        private const int MaxDepth = 32;
        private const int MaxSymbols = 100_000;
        private const int MaxStrings = 100_000;

        private readonly byte[] data;
        private int offset = 2;

        internal MarshalWalker(byte[] data)
        {
            this.data = data;
        }

        internal long SymbolCount { get; private set; }

        internal long StringCount { get; private set; }

        internal long ObjectCount { get; private set; }

        internal bool Stopped { get; private set; }

        internal List<string> Strings { get; } = [];

        internal void Run() => ReadValue(0);

        private void ReadValue(int depth)
        {
            if (Stopped)
            {
                return;
            }

            if (depth > MaxDepth || offset >= data.Length)
            {
                Stopped = true;
                return;
            }

            switch ((char)data[offset++])
            {
                case '0':
                case 'T':
                case 'F':
                    return;
                case 'i':
                    if (!TryReadFixnum(out _))
                    {
                        Stopped = true;
                    }

                    return;
                case ':':
                    ReadSymbol();
                    return;
                case ';':
                    ReadSymbol();
                    return;
                case '@':
                    if (!TryReadFixnum(out _))
                    {
                        Stopped = true;
                    }

                    return;
                case '"':
                    ReadString();
                    return;
                case 'f':
                    ReadString();
                    return;
                case 'l':
                    ReadBignum();
                    return;
                case '[':
                    ReadContainer(depth, isObject: false);
                    return;
                case '{':
                    ReadContainer(depth, isObject: false);
                    return;
                case 'o':
                    ReadObject(depth);
                    return;
                case 'S':
                case 'u':
                    ReadObject(depth);
                    return;
                case 'c':
                case 'm':
                    ReadSymbol();
                    return;
                case 'C':
                    ReadSymbol();
                    ReadValue(depth + 1);
                    return;
                case 'e':
                    ReadSymbol();
                    ReadValue(depth + 1);
                    return;
                case 'I':
                    ReadValue(depth + 1);
                    ReadSymbol();
                    ReadValue(depth + 1);
                    return;
                case 'U':
                    ReadSymbol();
                    if (TryReadFixnum(out var length) && length >= 0 && length <= data.Length - offset)
                    {
                        offset += (int)length;
                        ReadSymbol();
                    }
                    else
                    {
                        Stopped = true;
                    }

                    return;
                case '/':
                    ReadString();
                    if (offset < data.Length)
                    {
                        offset++;
                    }

                    return;
                default:
                    Stopped = true;
                    return;
            }
        }

        private void ReadSymbol()
        {
            if (!TryReadFixnum(out var length) || length < 0 || length > 4096 || length > data.Length - offset)
            {
                Stopped = true;
                return;
            }

            offset += (int)length;
            SymbolCount++;
        }

        private void ReadString()
        {
            if (!TryReadFixnum(out var length) || length < 0 || length > 1_000_000 || length > data.Length - offset)
            {
                Stopped = true;
                return;
            }

            StringCount++;
            if (StringCount > MaxStrings)
            {
                Stopped = true;
                return;
            }

            var span = data.AsSpan(offset, (int)length);
            offset += (int)length;
            var text = Encoding.UTF8.GetString(span);
            if (text.Length >= 2 && text.Length <= 120 && IsPrintable(text) && Strings.Count < GameDevLimits.MaxListEntryCount)
            {
                Strings.Add(text);
            }
        }

        private void ReadBignum()
        {
            if (offset >= data.Length)
            {
                Stopped = true;
                return;
            }

            offset++;
            if (!TryReadFixnum(out var length) || length < 0 || length > 1024 || length > data.Length - offset)
            {
                Stopped = true;
                return;
            }

            offset += (int)length;
        }

        private void ReadContainer(int depth, bool isObject)
        {
            if (!TryReadFixnum(out var count) || count < 0 || count > 1_000_000)
            {
                Stopped = true;
                return;
            }

            if (isObject)
            {
                ObjectCount++;
            }

            for (var index = 0; index < count && !Stopped; index++)
            {
                ReadValue(depth + 1);
            }
        }

        private void ReadObject(int depth)
        {
            ReadSymbol();
            if (Stopped)
            {
                return;
            }

            if (!TryReadFixnum(out var count) || count < 0 || count > 1_000_000)
            {
                Stopped = true;
                return;
            }

            ObjectCount++;
            if (SymbolCount > MaxSymbols)
            {
                Stopped = true;
                return;
            }

            for (var index = 0; index < count && !Stopped; index++)
            {
                ReadSymbol();
                ReadValue(depth + 1);
            }
        }

        private bool TryReadFixnum(out long value)
        {
            value = 0;
            if (offset >= data.Length)
            {
                return false;
            }

            var first = data[offset++];
            if (first == 0)
            {
                return true;
            }

            if (first is >= 5 and < 128)
            {
                value = first - 5;
                return true;
            }

            if (first is >= 1 and <= 4)
            {
                long positive = 0;
                for (var index = 0; index < first; index++)
                {
                    positive |= (long)data[offset++] << (8 * index);
                }

                value = positive;
                return true;
            }

            if (first < 0xFC)
            {
                value = first - 0x100;
                return true;
            }

            var length = 0x100 - first;
            if (length > data.Length - offset)
            {
                return false;
            }

            long number = 0;
            for (var index = 0; index < length; index++)
            {
                number |= (long)data[offset++] << (8 * index);
            }

            value = number - (1L << (8 * length));
            return true;
        }

        private static bool IsPrintable(string text)
        {
            foreach (var character in text)
            {
                if (char.IsControl(character) && character is not ('\t' or '\n' or '\r'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
