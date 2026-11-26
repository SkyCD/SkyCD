using System.Linq;
using System.Text;

namespace SkyCD.Plugin.Metadata.Html.Tests;

/// <summary>Builds HTML sources in memory so each expected value is known to the test asserting it.</summary>
internal static class HtmlFixtures
{
    public static byte[] Text(string html) => new UTF8Encoding(false).GetBytes(html);

    public static string FullDocument => """
        <!DOCTYPE html>
        <html lang="en-US">
        <head>
        <meta charset="utf-8">
        <title>  Quarterly Report  </title>
        <meta name="description" content="Q3 figures">
        <meta name="keywords" content="finance, report">
        <meta name="author" content="Finance Team">
        <meta name="generator" content="SkyCD 3.0">
        <base href="https://example.com/reports/">
        </head>
        <body>
        <h1>Quarterly</h1>
        <p>Body text with <a href="https://example.com/a">a link</a> and <a href="/b">another</a>.</p>
        <img src="logo.png" alt="logo">
        <img src="chart.png">
        <script src="app.js"></script>
        <script>var x = 1;</script>
        </body>
        </html>
        """;

    public static byte[] Full() => Text(FullDocument);

    public static byte[] Fragment() =>
        Text("<p>Just a <a href=\"x\">fragment</a> with no envelope.</p>");

    public static byte[] EmptyHead() =>
        Text("<html><head></head><body><p>Hi</p></body></html>");

    public static byte[] Utf8() =>
        Text("<html><head><title>H\u00e9llo</title></head><body><p>Caf\u00e9 na\u00efve</p></body></html>");

    public static byte[] LegacyCharset()
    {
        const string html = """
            <html><head><meta http-equiv="Content-Type" content="text/html; charset=windows-1252"><title>Café prices</title></head><body><p>naïve rates €</p></body></html>
            """;
        return Encoding.GetEncoding(1252).GetBytes(html);
    }

    public static byte[] WithNullByte() => [(byte)'a', 0, (byte)'b'];

    public static byte[] DeeplyNested() =>
        Text("<div>" + string.Concat(Enumerable.Repeat("<div>", 5000)) + "x" +
            string.Concat(Enumerable.Repeat("</div>", 5000)));
}
