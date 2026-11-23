using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace SkyCD.Plugin.Metadata.AbiWord.Tests;

/// <summary>Builds AbiWord XML and gzip containers in memory so preview tests need no binary samples.</summary>
internal static class AbiWordFixtures
{
    public const int MaxDepth = 64;

    public static string Document(string body, string metadata) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE abiword PUBLIC "-//ABISOURCE//DTD AWML 1.0 Strict//EN" "http://www.abisource.com/awml.dtd">
        <abiword xmlns="http://www.abisource.com/awml.dtd" version="1.0">
          {metadata}
          <section>{body}</section>
        </abiword>
        """;

    public static string DocumentWithTitle(string title, string body) =>
        Document(body, $"<metadata><m key=\"dc.title\">{title}</m></metadata>");

    public static string Paragraph(string text) => $"<p>{text}</p>";

    public static string RunParagraph(params string[] runs) =>
        "<p>" + string.Concat(runs.Select(run => $"<c style=\"Normal\">{run}</c>")) + "</p>";

    public static string EmptyParagraph => "<p/>";

    public static string Table(string cell) =>
        $"<table><cell><p>{cell}</p></cell></table>";

    public static string EmptyMetadata => "<metadata/>";

    public static string EmbeddedImage => "<image dataid=\"1\" props=\"0\"/>";

    public static string RepeatParagraphs(int count, string text) =>
        string.Concat(Enumerable.Repeat(Paragraph(text), count));

    public static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);

    public static byte[] Plain(string body, string metadata = "") => Bytes(Document(body, metadata));

    public static byte[] Compressed(string body, string metadata = "") => Gzip(Plain(body, metadata));

    public static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }
}
