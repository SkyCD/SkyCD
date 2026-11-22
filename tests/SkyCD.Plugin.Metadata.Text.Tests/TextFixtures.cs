using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Text.Tests;

/// <summary>
/// In-memory <see cref="IMediaSource"/> that charges reads exactly like the host does, so tests exercise the
/// same bounded random-access contract a plugin sees in production.
/// </summary>
internal sealed class ByteArrayMediaSource : IMediaSource
{
    private readonly byte[] data;
    private readonly long budget;
    private long charged;

    public ByteArrayMediaSource(byte[] data, long budget = 16 * 1024 * 1024)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.budget = budget;
    }

    public long ChargedBytes => charged;

    public int ReadCount { get; private set; }

    public long Length => data.Length;

    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;

        if (offset < 0 || count < 0 || offset > Length || count > Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "The requested range is outside the source.");
        }

        if (count > budget - charged)
        {
            throw new InvalidOperationException("The source read budget is exhausted.");
        }

        charged += count;
        return ValueTask.FromResult<ReadOnlyMemory<byte>>(new ReadOnlyMemory<byte>(data, (int)offset, count));
    }
}

/// <summary>
/// Builds text fixtures in every encoding the plugin claims to detect, plus the awkward inputs the allow-list
/// attracts: a huge single line, mixed line endings, binary bytes renamed to a text extension.
/// </summary>
internal static class TextFixtures
{
    internal const string Sample = "First line\nSecond line with é and ☕\nThird line";

    internal static byte[] Utf8(string text = Sample) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);

    /// <summary>
    /// UTF-8 with a real byte order mark. <see cref="Encoding.GetBytes(string)"/> never emits the preamble, so
    /// it has to be prepended explicitly or the fixture would be indistinguishable from plain UTF-8.
    /// </summary>
    internal static byte[] Utf8WithBom(string text = Sample) =>
        Concat(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));

    internal static byte[] Utf16Le(string text = Sample) =>
        Concat([0xFF, 0xFE], Encoding.Unicode.GetBytes(text));

    internal static byte[] Utf16Be(string text = Sample) =>
        Concat([0xFE, 0xFF], Encoding.BigEndianUnicode.GetBytes(text));

    /// <summary>
    /// Bytes that are valid ISO-8859-1 but not valid UTF-8, which is what forces the documented fallback
    /// rather than a silent replacement-character decode.
    /// </summary>
    internal static byte[] Latin1(string text = "Café résumé") => Encoding.Latin1.GetBytes(text);

    internal static byte[] Empty() => [];

    internal static byte[] Markdown(string title = "Release Notes", string body = "Some *emphasis* and a [link](https://example.com).") =>
        Utf8($"# {title}\n\n{body}\n\n## Section\n\nMore text.\n");

    internal static byte[] Csv() =>
        Utf8("name,size,kind\nalpha,10,file\nbeta,20,folder\n");

    /// <summary>One very long line with no terminator, which a naive line reader would buffer whole.</summary>
    internal static byte[] HugeSingleLine(int characterCount) =>
        Utf8(new string('x', characterCount));

    internal static byte[] Html() =>
        Utf8("<!DOCTYPE html>\n<html><head><title>Page</title><script>alert('x')</script></head>\n<body><a href=\"https://example.com\">link</a></body></html>\n");

    /// <summary>Binary content carrying a text extension: the case the plugin must refuse to display.</summary>
    internal static byte[] BinaryMasqueradingAsText(int length = 512)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            // Mostly NUL and C0 controls, with a plausible-looking text extension on the catalog entry.
            bytes[i] = (i % 4) switch { 0 => 0x00, 1 => 0x01, 2 => 0x1B, _ => (byte)(i % 251) };
        }

        return bytes;
    }

    internal static byte[] WithLineEndings(string ending, int lines)
    {
        var builder = new StringBuilder();
        for (var i = 1; i <= lines; i++)
        {
            builder.Append("line ").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(ending);
        }

        return Utf8(builder.ToString());
    }

    internal static byte[] MixedLineEndings() => Utf8("a\r\nb\nc\rd\n");

    internal static byte[] Concat(byte[] prefix, byte[] body)
    {
        var combined = new byte[prefix.Length + body.Length];
        prefix.CopyTo(combined, 0);
        body.CopyTo(combined, prefix.Length);
        return combined;
    }
}
