using System;
using System.Text;

namespace SkyCD.Plugin.Metadata.Markdown.Tests;

/// <summary>Builds Markdown sources in memory so each expected value is known to the test asserting it.</summary>
internal static class MarkdownFixtures
{
    public static byte[] Text(string markdown) => new UTF8Encoding(false).GetBytes(markdown);

    public static byte[] WithFrontMatter(string frontMatter, string body) =>
        Text($"---\n{frontMatter}\n---\n{body}");

    public static byte[] Utf16(string markdown) =>
        [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(markdown)];

    public static byte[] InvalidUtf8() => [0xC3, 0x28];

    public static byte[] WithNullByte() => [(byte)'a', 0, (byte)'b'];
}
