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
/// inputs. The redistributable <c>sample.zdc</c> fixture is checked into
/// <c>Fixtures/</c> instead (see <c>Fixtures/README.md</c>).
/// </summary>
public static class ZdcFixture
{
    public const string DiscGuidA = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
    public const string DiscGuidB = "6ba7b810-9dad-11d1-80b4-00c04fd430c8";
    public const string DiscGuidC = "6ba7b811-9dad-11d1-80b4-00c04fd430c8";

    public static ZdcMember IndexMember(string xmlPayload) => new()
    {
        FileName = ZdcFormat.IndexMemberName,
        Payload = Encoding.ASCII.GetBytes(xmlPayload)
    };

    public static ZdcMember DiscMember(string guid, string xmlPayload, uint fileType = 0, uint entryType = 0) => new()
    {
        FileName = $"{guid}.xml",
        Payload = Encoding.ASCII.GetBytes(xmlPayload),
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
        builder.Append("<?xml version=\"1.0\"?><catalog>");
        foreach (var guid in discGuids)
        {
            builder.Append($"<disc id=\"{guid}\"/>");
        }

        builder.Append("</catalog>");
        return builder.ToString();
    }

    public static string DiscXml(string guid) =>
        $"<?xml version=\"1.0\"?><disc id=\"{guid}\"><volume label=\"DISC-{guid[..8]}\"/></disc>";

    public static ZdcDatabase SampleDatabase() => new()
    {
        Members =
        {
            IndexMember(IndexXml(DiscGuidA, DiscGuidB)),
            DiscMember(DiscGuidA, DiscXml(DiscGuidA)),
            DiscMember(DiscGuidB, DiscXml(DiscGuidB), fileType: 1, entryType: 2),
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
            FormatId = "cdcatalogexpert-zdc",
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
