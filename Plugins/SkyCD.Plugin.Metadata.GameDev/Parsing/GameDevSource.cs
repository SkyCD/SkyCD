using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.GameDev.Exceptions;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>Bounded byte, text and archive helpers shared by the engine readers.</summary>
internal static class GameDevSource
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    /// <summary>Reads up to <paramref name="maxBytes"/> leading bytes and reports whether the source is longer.</summary>
    internal static async Task<(byte[] Data, bool Truncated)> ReadPrefixAsync(
        IMediaSource source, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var length = (int)Math.Min(source.Length, maxBytes);
        var data = new byte[length];
        var read = 0;
        while (read < length)
        {
            var chunk = await source.ReadAsync(read, Math.Min(length - read, 1 << 20), cancellationToken)
                .ConfigureAwait(false);
            if (chunk.Length == 0)
            {
                break;
            }

            chunk.Span.CopyTo(data.AsSpan(read));
            read += chunk.Length;
        }

        return read == length ? (data, source.Length > length) : (data[..read], true);
    }

    /// <summary>Reads the whole source when it fits the bound; otherwise reports truncation.</summary>
    internal static Task<(byte[] Data, bool Truncated)> ReadWholeAsync(
        IMediaSource source, int maxBytes, CancellationToken cancellationToken) =>
        ReadPrefixAsync(source, maxBytes, cancellationToken);

    /// <summary>Decodes UTF-8 text and strips a byte order mark.</summary>
    internal static string DecodeText(byte[] data)
    {
        var text = StrictUtf8.GetString(data);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    /// <summary>Decodes a bounded prefix as text.</summary>
    internal static string DecodeText(ReadOnlySpan<byte> data) => StrictUtf8.GetString(data);

    /// <summary>Reads a ZIP entry into memory under a fixed bound.</summary>
    internal static byte[] ReadEntry(ZipArchiveEntry entry, int maxBytes)
    {
        long length = entry.Length;
        if (length <= 0)
        {
            return [];
        }

        if (length > maxBytes)
        {
            throw new GameDevLimitExceededException();
        }

        using var stream = entry.Open();
        using var memory = new MemoryStream((int)length);
        stream.CopyTo(memory, maxBytes + 1);
        if (memory.Length > maxBytes)
        {
            throw new GameDevLimitExceededException();
        }

        return memory.ToArray();
    }

    /// <summary>Opens a ZIP container from the bounded source; callers dispose it.</summary>
    internal static ZipArchive OpenZip(IMediaSource source, CancellationToken cancellationToken) =>
        new(new MediaSourceStream(source, cancellationToken), ZipArchiveMode.Read, leaveOpen: false);

    /// <summary>Finds the entry whose full name equals <paramref name="name"/>, ignoring directory case.</summary>
    internal static ZipArchiveEntry? FindEntry(ZipArchive archive, string name)
    {
        foreach (var entry in archive.Entries)
        {
            if (string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>Returns the last extension part of a ZIP entry name, lower-cased, including the dot.</summary>
    internal static string EntryExtension(string name)
    {
        var slash = name.LastIndexOfAny(['/', '\\']);
        var dot = name.LastIndexOf('.');
        return dot > slash ? name[dot..].ToLowerInvariant() : string.Empty;
    }

    /// <summary>True when the bytes start with the ASCII text, case-insensitively.</summary>
    internal static bool StartsWithAscii(ReadOnlySpan<byte> data, string text)
    {
        if (data.Length < text.Length)
        {
            return false;
        }

        for (var index = 0; index < text.Length; index++)
        {
            var value = data[index];
            if (value >= 'A' && value <= 'Z')
            {
                value = (byte)(value + 32);
            }

            var expected = text[index];
            if (expected >= 'A' && expected <= 'Z')
            {
                expected = (char)(expected + 32);
            }

            if (value != expected)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when the bytes contain the ASCII text anywhere in the window.</summary>
    internal static bool ContainsAscii(ReadOnlySpan<byte> data, string text) =>
        data.IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;

    /// <summary>True when the bytes start with the sequence.</summary>
    internal static bool StartsWith(ReadOnlySpan<byte> data, params byte[] prefix) =>
        data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);

    /// <summary>Renders the first line of a text document, trimmed and bounded.</summary>
    internal static string FirstLine(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var line = end >= 0 ? text[..end] : text;
        return line.Length > 200 ? line[..200] : line.Trim();
    }

    /// <summary>Counts non-overlapping occurrences of a literal in a string.</summary>
    internal static long CountOccurrences(string text, string value)
    {
        long count = 0;
        var offset = 0;
        while (true)
        {
            var index = text.IndexOf(value, offset, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            offset = index + value.Length;
        }
    }

    /// <summary>Copies a byte range without throwing when the range is partly outside the buffer.</summary>
    internal static ReadOnlyMemory<byte> Slice(byte[] data, long offset, long count)
    {
        if (offset < 0 || offset >= data.Length || count <= 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        var start = (int)offset;
        var length = (int)Math.Min(count, data.Length - start);
        return new ReadOnlyMemory<byte>(data, start, length);
    }
}
