using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.EBook.Models;

namespace SkyCD.Plugin.Metadata.EBook.Tests;

/// <summary>
/// Builds e-books in memory: ZIP-based EPUB/iBook packages, Palm database Mobipocket/PalmDOC books and
/// FictionBook XML. Tests never touch the file system, and every fixture is a syntactically valid document
/// for its format so the readers are exercised on real structure rather than on mocks.
/// </summary>
internal static class EBookFixtures
{
    public const string ContainerXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles>
            <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
          </rootfiles>
        </container>
        """;

    public const string Epub2Opf = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="uid">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:title>The Long Way</dc:title>
            <dc:creator>Becky Chambers</dc:creator>
            <dc:creator>Ann Leckie</dc:creator>
            <dc:publisher>Hodder &amp; Stoughton</dc:publisher>
            <dc:language>en-GB</dc:language>
            <dc:subject>Science Fiction</dc:subject>
            <dc:description>A young woman leaves Earth behind.</dc:description>
            <dc:identifier id="uid">URN:ISBN:9781473601801</dc:identifier>
            <dc:date>2015-07-30</dc:date>
            <meta name="cover" content="cover-img"/>
          </metadata>
          <manifest>
            <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
            <item id="cover-img" href="images/cover.jpg" media-type="image/jpeg"/>
            <item id="chapter" href="chapter1.xhtml" media-type="application/xhtml+xml"/>
            <item id="font" href="fonts/book.otf" media-type="application/vnd.ms-opentype"/>
          </manifest>
          <spine toc="ncx">
            <itemref idref="chapter"/>
          </spine>
          <guide>
            <reference type="cover" href="images/cover.jpg"/>
          </guide>
        </package>
        """;

    public const string Epub3Opf = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="pub-id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:title>A Closed and Common Orbit</dc:title>
            <dc:creator>Becky Chambers</dc:creator>
            <dc:language>en</dc:language>
            <dc:identifier id="pub-id">urn:uuid:0d7b6c8e-0000-0000-0000-000000000000</dc:identifier>
          </metadata>
          <manifest>
            <item id="art" href="art/front.png" media-type="image/png" properties="cover-image"/>
          </manifest>
          <spine/>
        </package>
        """;

    public static byte[] Jpeg(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        return data.ToArray();
    }

    public static byte[] Png(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    public static byte[] Zip(params (string Name, byte[] Data)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                using var stream = entry.Open();
                stream.Write(data);
            }
        }

        return buffer.ToArray();
    }

    public static byte[] Epub(
        string opf = Epub2Opf,
        string opfPath = "OEBPS/content.opf",
        bool includeContainer = true,
        byte[]? cover = null,
        string coverPath = "OEBPS/images/cover.jpg",
        string? encryptionXml = null,
        params (string Name, byte[] Data)[] extra)
    {
        var entries = new List<(string, byte[])>
        {
            ("mimetype", Encoding.ASCII.GetBytes("application/epub+zip")),
            (opfPath, Encoding.UTF8.GetBytes(opf))
        };
        if (includeContainer)
        {
            entries.Add(("META-INF/container.xml", Encoding.UTF8.GetBytes(ContainerXml)));
        }

        if (cover is not null)
        {
            entries.Add((coverPath, cover));
        }

        if (encryptionXml is not null)
        {
            entries.Add(("META-INF/encryption.xml", Encoding.UTF8.GetBytes(encryptionXml)));
        }

        entries.AddRange(extra);
        return Zip([.. entries]);
    }

    public static byte[] FontObfuscationEncryption() => """
        <?xml version="1.0" encoding="utf-8"?>
        <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container"
                    xmlns:enc="http://www.w3.org/2001/04/xmlenc#">
          <enc:EncryptedData>
            <enc:EncryptionMethod Algorithm="http://www.idpf.org/2008/embedding"/>
            <enc:CipherData>
              <enc:CipherReference URI="OEBPS/fonts/book.otf"/>
            </enc:CipherData>
          </enc:EncryptedData>
        </encryption>
        """u8.ToArray();

    public static byte[] ContentEncryption() => """
        <?xml version="1.0" encoding="utf-8"?>
        <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container"
                    xmlns:enc="http://www.w3.org/2001/04/xmlenc#">
          <enc:EncryptedData>
            <enc:EncryptionMethod Algorithm="http://www.w3.org/2001/04/xmlenc#aes128-cbc"/>
            <enc:CipherData>
              <enc:CipherReference URI="OEBPS/chapter1.xhtml"/>
            </enc:CipherData>
          </enc:EncryptedData>
        </encryption>
        """u8.ToArray();

    public static string Fb2Xml(byte[]? cover = null, bool includeCoverPage = true) => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0" xmlns:l="http://www.w3.org/1999/xlink">
          <description>
            <title-info>
              <genre>sf</genre>
              <author><first-name>Arkady</first-name><last-name>Strugatsky</last-name></author>
              <author><first-name>Boris</first-name><last-name>Strugatsky</last-name></author>
              <book-title>Roadside Picnic</book-title>
              <annotation>Aliens visited and left their rubbish behind.</annotation>
              <date value="1972-01-01">1972</date>
              {{(includeCoverPage ? "<coverpage><image l:href=\"#cover.jpg\"/></coverpage>" : string.Empty)}}
              <lang>ru</lang>
            </title-info>
            <publish-info>
              <publisher>Molodaya Gvardiya</publisher>
              <year>1972</year>
              <isbn>978-5-17-000000-0</isbn>
            </publish-info>
            <document-info>
              <id>fb2-document-id</id>
            </document-info>
          </description>
          <body>
            <section><p>Text.</p></section>
          </body>
          {{(cover is null ? string.Empty : $"<binary id=\"cover.jpg\" content-type=\"image/jpeg\">{Convert.ToBase64String(cover)}</binary>")}}
        </FictionBook>
        """;

    public static byte[] Mobi(
        string? title = "The Long Way",
        string? fullName = "The Long Way to a Small Angry Planet",
        string[]? authors = null,
        string? publisher = "Hodder & Stoughton",
        string? description = "A young woman leaves Earth behind.",
        string? isbn = "9781473601801",
        string? subject = "Science Fiction",
        string? date = "2015-07-30",
        string? language = "en",
        byte[]? cover = null,
        int encryptionType = 0,
        bool kf8 = false,
        bool fakeCover = false,
        bool withExth = true,
        string databaseName = "Test Book")
    {
        authors ??= ["Becky Chambers"];
        const int mobiHeaderLength = 232;

        var exth = new List<byte[]>();
        if (withExth)
        {
            foreach (var author in authors)
            {
                exth.Add(ExthRecord(100, Encoding.UTF8.GetBytes(author)));
            }

            AddText(101, publisher);
            AddText(103, description);
            AddText(104, isbn);
            AddText(105, subject);
            AddText(106, date);
            AddText(524, language);
            AddText(503, title);
            if (kf8)
            {
                exth.Add(ExthRecord(121, U32(0xFFFFFFFF)));
            }

            if (cover is not null)
            {
                exth.Add(ExthRecord(201, U32(0)));
            }

            if (fakeCover)
            {
                exth.Add(ExthRecord(203, U32(1)));
            }
        }

        var exthBytes = exth.Count == 0 ? [] : BuildExth(exth);
        var nameBytes = Encoding.UTF8.GetBytes(fullName ?? string.Empty);
        var recordZero = new byte[16 + mobiHeaderLength + exthBytes.Length + nameBytes.Length];

        // PalmDOC header.
        WriteU16(recordZero, 0, 1);
        WriteU32(recordZero, 4, 64);
        WriteU16(recordZero, 8, 2);
        WriteU16(recordZero, 12, (ushort)encryptionType);

        // MOBI header.
        Encoding.ASCII.GetBytes("MOBI").CopyTo(recordZero, 16);
        WriteU32(recordZero, 20, mobiHeaderLength);
        WriteU32(recordZero, 24, 2);
        WriteU32(recordZero, 28, 65001);
        WriteU32(recordZero, 32, 0x1234);
        WriteU32(recordZero, 36, kf8 ? 8u : 6u);
        WriteU32(recordZero, 16 + 0x40, 2);
        WriteU32(recordZero, 16 + 0x44, (uint)(16 + mobiHeaderLength + exthBytes.Length));
        WriteU32(recordZero, 16 + 0x48, (uint)nameBytes.Length);
        WriteU32(recordZero, 16 + 0x4C, 9);
        WriteU32(recordZero, 16 + 0x5C, 2);
        WriteU32(recordZero, 16 + 0x70, withExth ? 0x40u : 0u);
        exthBytes.CopyTo(recordZero, 16 + mobiHeaderLength);
        nameBytes.CopyTo(recordZero, 16 + mobiHeaderLength + exthBytes.Length);

        var recordsToWrite = new List<byte[]> { recordZero, Encoding.ASCII.GetBytes("compressed text") };
        if (cover is not null)
        {
            recordsToWrite.Add(cover);
        }

        return BuildPalmDatabase(databaseName, "BOOK", "MOBI", recordsToWrite);

        void AddText(uint type, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                exth.Add(ExthRecord(type, Encoding.UTF8.GetBytes(value)));
            }
        }
    }

    public static byte[] PalmDoc(int encryptionType = 0, string databaseName = "Old Notes")
    {
        var text = Encoding.ASCII.GetBytes("plain text");
        var record = new byte[16 + text.Length];
        WriteU16(record, 0, 1);
        WriteU32(record, 4, (uint)text.Length);
        WriteU16(record, 8, 1);
        WriteU16(record, 12, (ushort)encryptionType);
        text.CopyTo(record, 16);

        return BuildPalmDatabase(databaseName, "TEXt", "REAd", [record]);
    }

    public static byte[] Truncate(byte[] data, int length) => data[..Math.Min(length, data.Length)];

    public static EBookSettings Settings(
        bool collectionEnabled = true,
        EBookNameSource nameSource = EBookNameSource.FileName,
        bool tagAuthors = false,
        bool extractCovers = false,
        string coverSize = "250",
        string? extensions = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [EBookSettings.CollectionEnabledKey] = collectionEnabled ? "true" : "false",
            [EBookSettings.NameSourceKey] = nameSource switch
            {
                EBookNameSource.Author => "author",
                EBookNameSource.Title => "title",
                _ => "filename"
            },
            [EBookSettings.TagAuthorsKey] = tagAuthors ? "true" : "false",
            [EBookSettings.ExtractCoversKey] = extractCovers ? "true" : "false",
            [EBookSettings.CoverSizeKey] = coverSize
        };
        if (extensions is not null)
        {
            values[EBookSettings.ExtensionsKey] = extensions;
        }

        return EBookSettings.From(values);
    }

    public static async Task<MediaExtractionResult> ExtractMetadataAsync(
        byte[] book,
        string extension = ".epub",
        EBookSettings? settings = null,
        EBookPluginOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var plugin = new EBookMetadataPlugin(options ?? EBookPluginOptions.Default);
        if (settings is not null)
        {
            plugin.ApplySettings(settings.ToDictionary());
        }

        return await plugin.ExtractAsync(
            new MediaItem(extension, null, book.Length),
            new ByteArrayMediaSource(book),
            progress: null,
            cancellationToken);
    }

    public static async Task<MediaPreviewResult> GenerateCoverAsync(
        byte[] book,
        EBookSettings? settings = null,
        EBookPluginOptions? options = null,
        string extension = ".epub",
        CancellationToken cancellationToken = default)
    {
        var plugin = new EBookCoverPlugin(options ?? EBookPluginOptions.Default);
        plugin.ApplySettings((settings ?? Settings(extractCovers: true)).ToDictionary());

        return await plugin.GeneratePreviewAsync(
            new MediaItem(extension, null, book.Length),
            new ByteArrayMediaSource(book),
            progress: null,
            cancellationToken);
    }

    public static SKBitmap Decode(ReadOnlyMemory<byte> data)
    {
        using var skData = SKData.CreateCopy(data.Span);
        return SKBitmap.Decode(skData) ?? throw new InvalidOperationException("The artifact is not a decodable image.");
    }

    public static string? Text(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Text;

    public static long? Integer(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Integer;

    public static DateTimeOffset? Timestamp(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Timestamp;

    public static bool? Boolean(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Boolean;

    private static byte[] BuildPalmDatabase(
        string name,
        string type,
        string creator,
        IReadOnlyList<byte[]> records)
    {
        const int headerBytes = 78;
        var headerSize = headerBytes + (records.Count * 8) + 2;
        var total = headerSize + records.Sum(record => record.Length);
        var database = new byte[total];

        var nameBytes = Encoding.Latin1.GetBytes(name);
        nameBytes[..Math.Min(32, nameBytes.Length)].CopyTo(database, 0);
        Encoding.ASCII.GetBytes(type).CopyTo(database, 60);
        Encoding.ASCII.GetBytes(creator).CopyTo(database, 64);
        WriteU16(database, 76, (ushort)records.Count);

        var offset = headerSize;
        for (var index = 0; index < records.Count; index++)
        {
            WriteU32(database, headerBytes + (index * 8), (uint)offset);
            records[index].CopyTo(database, offset);
            offset += records[index].Length;
        }

        return database;
    }

    private static byte[] BuildExth(IReadOnlyList<byte[]> records)
    {
        var contentLength = 12 + records.Sum(record => record.Length);
        var paddedLength = (contentLength + 3) & ~3;
        var block = new byte[paddedLength];
        Encoding.ASCII.GetBytes("EXTH").CopyTo(block, 0);
        WriteU32(block, 4, (uint)paddedLength);
        WriteU32(block, 8, (uint)records.Count);
        var offset = 12;
        foreach (var record in records)
        {
            record.CopyTo(block, offset);
            offset += record.Length;
        }

        return block;
    }

    private static byte[] ExthRecord(uint type, byte[] data)
    {
        var record = new byte[8 + data.Length];
        WriteU32(record, 0, type);
        WriteU32(record, 4, (uint)record.Length);
        data.CopyTo(record, 8);
        return record;
    }

    private static byte[] U32(uint value)
    {
        var data = new byte[4];
        WriteU32(data, 0, value);
        return data;
    }

    private static void WriteU16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }
}
