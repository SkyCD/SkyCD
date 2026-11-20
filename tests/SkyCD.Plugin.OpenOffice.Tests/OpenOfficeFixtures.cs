using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SkyCD.Plugin.OpenOffice.Tests;

/// <summary>
/// Builds structurally valid OpenDocument and legacy OpenOffice.org packages in memory, so the suite commits
/// no binary blobs and every expected value is one the fixture wrote.
/// </summary>
internal static class OpenOfficeFixtures
{
    public const string OdfTextContentType = "application/vnd.oasis.opendocument.text";
    public const string OdfSpreadsheetContentType = "application/vnd.oasis.opendocument.spreadsheet";
    public const string LegacyWriterContentType = "application/vnd.sun.xml.writer";

    private const string OdfOfficeNamespace = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string OdfMetaNamespace = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
    private const string LegacyOfficeNamespace = "http://openoffice.org/2000/office";
    private const string LegacyMetaNamespace = "http://openoffice.org/2000/meta";
    private const string OdfManifestNamespace = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
    private const string DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";
    private const string XLinkNamespace = "http://www.w3.org/1999/xlink";

    public static byte[] OdfDocument(
        string? metaXml = null,
        string contentType = OdfTextContentType,
        bool includeMimeType = true,
        bool includeMetadata = true,
        string? manifestXml = null)
    {
        return BuildPackage(
            contentType,
            includeMimeType,
            includeMetadata ? metaXml ?? OdfMetadata(FullMetadata()) : null,
            manifestXml);
    }

    public static byte[] LegacyDocument(string? metaXml = null, string contentType = LegacyWriterContentType) =>
        BuildPackage(contentType, includeMimeType: true,
            metaXml ?? LegacyMetadata(FullMetadata("Legacy Report")), manifestXml: null);

    public static string OdfMetadata(string body, string version = "1.2") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-meta xmlns:office="{OdfOfficeNamespace}" xmlns:meta="{OdfMetaNamespace}" xmlns:dc="{DublinCoreNamespace}" xmlns:xlink="{XLinkNamespace}" office:version="{version}">
          <office:meta>{body}</office:meta>
        </office:document-meta>
        """;

    public static string LegacyMetadata(string body, string version = "1.0") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-meta xmlns:office="{LegacyOfficeNamespace}" xmlns:meta="{LegacyMetaNamespace}" xmlns:dc="{DublinCoreNamespace}" xmlns:xlink="{XLinkNamespace}" office:version="{version}">
          <office:meta>{body}</office:meta>
        </office:document-meta>
        """;

    public static string FullMetadata(string title = "Annual Report") => $"""
        <dc:title>{title}</dc:title>
        <dc:subject>Budget</dc:subject>
        <dc:description>Line one
        Line two</dc:description>
        <dc:creator>A. Writer</dc:creator>
        <meta:initial-creator>Initial Writer</meta:initial-creator>
        <meta:generator>LibreOffice/7.6.4.1</meta:generator>
        <dc:language>en-GB</dc:language>
        <dc:rights>Internal use only</dc:rights>
        <dc:publisher>SkyCD Publishing</dc:publisher>
        <dc:contributor>Editor</dc:contributor>
        <dc:identifier>DOC-2024-17</dc:identifier>
        <dc:source>Intranet</dc:source>
        <dc:relation>Series A</dc:relation>
        <dc:coverage>2024</dc:coverage>
        <dc:type>Report</dc:type>
        <dc:format>application/vnd.oasis.opendocument.text</dc:format>
        <meta:template xlink:href="file:///templates/report.ott"><meta:date>2024-02-01T00:00:00Z</meta:date>Report template</meta:template>
        <meta:printed-by>Print Office</meta:printed-by>
        <meta:creation-date>2024-03-01T12:30:15Z</meta:creation-date>
        <dc:date>2024-06-04T08:09:10+02:00</dc:date>
        <meta:print-date>2024-07-05T10:11:12</meta:print-date>
        <meta:editing-cycles>7</meta:editing-cycles>
        <meta:editing-duration>PT2H30M</meta:editing-duration>
        <meta:keyword>finance</meta:keyword>
        <meta:keyword>annual</meta:keyword>
        <meta:document-statistic meta:page-count="10" meta:table-count="2" meta:image-count="3" meta:object-count="4" meta:paragraph-count="55" meta:word-count="1200" meta:character-count="7000" meta:cell-count="0" meta:sheet-count="0"/>
        """;

    public static string Manifest(bool encrypted)
    {
        var encryption = encrypted
            ? """
              <manifest:encryption-data manifest:checksum-type="SHA1/1K" manifest:checksum="AAAA">
                <manifest:algorithm manifest:algorithm-name="Blowfish CFB" manifest:initialisation-vector="AAAA"/>
                <manifest:start-key-generation manifest:start-key-generation-name="SHA1" manifest:key-size="20"/>
              </manifest:encryption-data>
              """
            : string.Empty;

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <manifest:manifest xmlns:manifest="{OdfManifestNamespace}" manifest:version="1.2">
              <manifest:file-entry manifest:full-path="/" manifest:media-type="{OdfTextContentType}"/>
              <manifest:file-entry manifest:full-path="meta.xml" manifest:media-type="text/xml">{encryption}</manifest:file-entry>
            </manifest:manifest>
            """;
    }

    public static string MetadataWithoutRoot(string body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <not-a-metadata-document xmlns:office="{OdfOfficeNamespace}" xmlns:dc="{DublinCoreNamespace}">{body}</not-a-metadata-document>
        """;

    public static string TruncatedMetadata() => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-meta xmlns:office="{OdfOfficeNamespace}" xmlns:meta="{OdfMetaNamespace}" xmlns:dc="{DublinCoreNamespace}">
          <office:meta><dc:title>Cut off
        """;

    public static string ExternalEntityMetadata() => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE office:document-meta [<!ENTITY payload SYSTEM "file:///definitely-not-read">]>
        <office:document-meta xmlns:office="{OdfOfficeNamespace}" xmlns:meta="{OdfMetaNamespace}" xmlns:dc="{DublinCoreNamespace}">
          <office:meta><dc:title>&payload;</dc:title></office:meta>
        </office:document-meta>
        """;

    public static byte[] NotAZip() => Encoding.ASCII.GetBytes("this is not an OpenOffice package");

    public static byte[] Truncated(byte[] package) => package[..(package.Length / 2)];

    private static byte[] BuildPackage(
        string contentType,
        bool includeMimeType,
        string? metadataXml,
        string? manifestXml)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (includeMimeType)
            {
                AddEntry(archive, "mimetype", contentType);
            }

            if (manifestXml is not null)
            {
                AddEntry(archive, "META-INF/manifest.xml", manifestXml);
            }

            if (metadataXml is not null)
            {
                AddEntry(archive, "meta.xml", metadataXml);
            }

            AddEntry(archive, "content.xml",
                $"<office:document-content xmlns:office=\"{OdfOfficeNamespace}\"/>");
        }

        return stream.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var entryStream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        entryStream.Write(bytes, 0, bytes.Length);
    }
}
