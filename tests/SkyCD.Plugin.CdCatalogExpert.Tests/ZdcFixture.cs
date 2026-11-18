using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.CdCatalogExpert;

namespace SkyCD.Plugin.CdCatalogExpert.Tests;

/// <summary>
/// Builds license-safe ZDC fixtures at runtime: synthetic containers
/// serialized with the plugin writer, plus byte-level mutations for malformed
/// inputs. The redistributable fixtures are checked into <c>Fixtures/</c>.
/// </summary>
public static class ZdcFixture
{
    public const string DiscGuidA = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
    public const string DiscGuidB = "6ba7b810-9dad-11d1-80b4-00c04fd430c8";
    public const string DiscGuidC = "6ba7b811-9dad-11d1-80b4-00c04fd430c8";
    public const string FormatId = "cd-catalog-expert-zdc";

    private static readonly Encoding Windows1252 = CreateWindows1252();

    public static ZdcMember IndexMember(string xmlPayload) => new()
    {
        FileName = ZdcFormat.IndexMemberName,
        Payload = EncodeXml(xmlPayload)
    };

    public static ZdcMember DiscMember(string guid, string xmlPayload, uint fileType = 0, uint entryType = 0) => new()
    {
        FileName = $"{guid}.xml",
        Payload = EncodeXml(xmlPayload),
        FileType = fileType,
        EntryType = entryType,
        TimestampA = 100,
        TimestampB = 200,
        TimestampC = 300
    };

    public static ZdcMember CoverMember(string guid, byte[] jpeg) => new()
    {
        FileName = $"{guid}.jpg",
        Payload = jpeg
    };

    public static byte[] EncodeXml(string text) => Windows1252.GetBytes(text);

    private static Encoding CreateWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    public static byte[] MinimalJpeg(int fillerBytes = 64)
    {
        var jpeg = new byte[fillerBytes + 4];
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;
        for (var i = 0; i < fillerBytes; i++)
        {
            jpeg[2 + i] = (byte)(i & 0xFF);
        }

        jpeg[^2] = 0xFF;
        jpeg[^1] = 0xD9;
        return jpeg;
    }

    public static string IndexXml(params string[] discGuids)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"windows-1252\"?><Z N=\"Catalog\">");
        foreach (var guid in discGuids)
        {
            builder.Append($"<L N=\"Disc {guid[..8]}\" F=\"{{{guid}}}\"/>");
        }

        builder.Append("</Z>");
        return builder.ToString();
    }

    public static string DiscXml(string guid) =>
        $"<?xml version=\"1.0\" encoding=\"windows-1252\"?><R><F N=\"file-{guid[..8]}.txt\" S=\"12\"/></R>";

    public static ZdcDatabase SampleDatabase() => new()
    {
        Members =
        {
            IndexMember(
                "<?xml version=\"1.0\" encoding=\"windows-1252\"?>"
                + "<Z N=\"Sample Catalog\" D=\"A sample catalog\">"
                + "<G N=\"Games\" C=\"1700000000\">"
                + $"<L N=\"Disc A\" F=\"{{{DiscGuidA}}}\" P=\"D:\\\" C=\"1700000001\" M=\"1700000002\" "
                + "D=\"Disc A description\" V=\"DISCA\" I=\"CDFS\" T=\"2\" E=\"-123456\" K=\"700000000\" R=\"1000\" "
                + "O=\"1\" SO_A=\"1\" SO_B=\"0\" SO_C=\"1\" SO_D=\"0\" SO_E=\"1\" SO_F=\"0\" SO_G=\"1\" "
                + "W=\"games, retro\" DS=\"Shelf 1\" DI=\"IDX-1\" DB=\"0001234567890\" DR=\"5\" "
                + $"G=\"{DiscGuidA}.jpg\"/>"
                + $"<L N=\"Disc B\" F=\"{{{DiscGuidB}}}\" V=\"DISCB\" I=\"UDF\" T=\"3\" E=\"7\" K=\"-1\" R=\"-1\"/>"
                + "</G>"
                + "</Z>"),
            DiscMember(
                DiscGuidA,
                "<?xml version=\"1.0\" encoding=\"windows-1252\"?>"
                + "<R><D N=\"Folder\" A=\"16\" M=\"1700000003\" C=\"1700000004\" B=\"1700000005\" "
                + "D=\"Folder description\" W=\"folder\">"
                + "<F N=\"readme.txt\" S=\"5\" A=\"32\" M=\"1700000006\" D=\"Read me\"/>"
                + "</D><D N=\"Empty\"/></R>"),
            DiscMember(
                DiscGuidB,
                $"<?xml version=\"1.0\" encoding=\"windows-1252\"?><R><F N=\"file-{DiscGuidB[..8]}.txt\" S=\"12\"/></R>",
                fileType: 1,
                entryType: 2),
            CoverMember(DiscGuidA, MinimalJpeg())
        }
    };

    public static ZdcDatabase EmptyPayloadDatabase() => new()
    {
        Members = { new ZdcMember { FileName = "empty.xml", Payload = [] } }
    };

    public static ZdcDatabase MultiChunkDatabase()
    {
        var database = new ZdcDatabase();
        for (var i = 0; i < 9; i++)
        {
            database.Members.Add(DiscMember($"0000000{i}-0000-0000-0000-000000000000", DiscXml($"0000000{i}-0000-0000-0000-000000000000")));
        }

        return database;
    }

    public static byte[] Serialize(ZdcDatabase database)
    {
        using var stream = new MemoryStream();
        ZdcWriter.Write(stream, database);
        return stream.ToArray();
    }

    public static MemoryStream StreamOf(byte[] bytes) => new(bytes, writable: false);

    public static FileFormatReadRequest ReadRequest(Stream source, string fileName = "catalog.zdc")
    {
        return new FileFormatReadRequest
        {
            Source = source,
            FormatId = FormatId,
            FileName = fileName,
            SourcePath = fileName
        };
    }

    public static byte[] Mutate(byte[] source, Action<byte[]> mutate)
    {
        var copy = (byte[])source.Clone();
        mutate(copy);
        return copy;
    }

    public static int ChunkOffset(int chunk) => ZdcFormat.ChunkBaseOffset + (chunk * ZdcFormat.ChunkSize);

    public static uint ReadUInt32(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    public static int ReadInt32(byte[] data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
}
