using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using SkyCD.Plugin.FileFormat.CDCatalogExpert.Models;
using SkyCD.Plugin.FileFormat.CDCatalogExpert.Parsing;

namespace SkyCD.Plugin.FileFormat.CDCatalogExpert.Tests;

public sealed class ZdcGoldenTests
{
    [Fact]
    public void XorWords_KnownVector_MatchesHandComputation()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        Assert.Equal(0x0C040404u, ZdcChecksums.XorWords(data, 0, data.Length));
    }

    [Fact]
    public void Crc16_StandardCheck_MatchesArcVector()
    {
        var data = Encoding.ASCII.GetBytes("123456789");
        Assert.Equal(0xBB3D, ZdcChecksums.Crc16(data, 0, data.Length));
    }

    [Fact]
    public void Serialize_SampleDatabase_HeaderOffsetsMatchSpec()
    {
        var bytes = ZdcFixture.Serialize(ZdcFixture.SampleDatabase());

        Assert.Equal(ZdcFormat.ContainerMagic, bytes[ZdcFormat.ContainerMagicOffset..(ZdcFormat.ContainerMagicOffset + 8)]);
        Assert.Equal(ZdcFormat.ProfileMagic, bytes[ZdcFormat.ProfileMagicOffset..(ZdcFormat.ProfileMagicOffset + 8)]);
        Assert.Equal((uint)ZdcFormat.ChunkSize, ZdcFixture.ReadUInt32(bytes, ZdcFormat.ChunkSizeOffset));
        Assert.Equal(ZdcFormat.FirstDirectoryChunk, ZdcFixture.ReadInt32(bytes, ZdcFormat.FirstDirectoryChunkOffset));
        Assert.Equal(4u, ZdcFixture.ReadUInt32(bytes, ZdcFormat.EntryCountOffset));

        var physicalChunks = (bytes.Length - ZdcFormat.ChunkBaseOffset) / ZdcFormat.ChunkSize;
        Assert.Equal((uint)physicalChunks, ZdcFixture.ReadUInt32(bytes, ZdcFormat.ChunkCountOffset));
        Assert.Equal(0, (bytes.Length - ZdcFormat.ChunkBaseOffset) % ZdcFormat.ChunkSize);
        Assert.All(bytes[ZdcFormat.ContainerKeyOffset..(ZdcFormat.ContainerKeyOffset + ZdcFormat.ContainerKeySize)], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Serialize_SampleDatabase_MemberRecordOffsetsMatchSpec()
    {
        var bytes = ZdcFixture.Serialize(ZdcFixture.SampleDatabase());
        var record = ZdcFixture.ChunkOffset(ZdcFormat.FirstDirectoryChunk) + ZdcFormat.ChunkHeaderSize;

        // First member sorts as zdc_index.xml with one data chunk: tree 4, index 5, data 6.
        Assert.Equal(5, ZdcFixture.ReadInt32(bytes, record));
        var storedLength = (int)((ulong)ZdcFixture.ReadUInt32(bytes, record + 0x04) | ((ulong)ZdcFixture.ReadUInt32(bytes, record + 0x08) << 32));
        Assert.True(storedLength > ZdcFormat.AacsHeaderSize);
        Assert.Equal(0u, ZdcFixture.ReadUInt32(bytes, record + 0x02C));
        Assert.Equal(ZdcFormat.IndexMemberName, Encoding.ASCII.GetString(bytes, record + 0x0E0, ZdcFormat.IndexMemberName.Length));
        Assert.Equal(0, bytes[record + 0x0E0 + ZdcFormat.IndexMemberName.Length]);
    }

    [Fact]
    public void Serialize_SampleDatabase_TreeChecksumCoversDataArea()
    {
        var bytes = ZdcFixture.Serialize(ZdcFixture.SampleDatabase());
        var chunk = ZdcFixture.ChunkOffset(ZdcFormat.FirstDirectoryChunk);
        var stored = ZdcFixture.ReadUInt32(bytes, chunk + 0x04);
        Assert.Equal(ZdcChecksums.XorWords(bytes, chunk + ZdcFormat.ChunkHeaderSize, ZdcFormat.DataAreaSize), stored);
    }

    [Fact]
    public void Serialize_SampleDatabase_AacsEnvelopeFieldsMatchSpec()
    {
        var bytes = ZdcFixture.Serialize(ZdcFixture.SampleDatabase());
        // Layout: tree chunk 4, index chunks 5..8, data chunks from 9.
        var dataChunk = ZdcFixture.ChunkOffset(9) + ZdcFormat.ChunkHeaderSize;

        Assert.Equal(ZdcFormat.AacsMagic, bytes[dataChunk..(dataChunk + 4)]);
        Assert.Equal(0u, ZdcFixture.ReadUInt32(bytes, dataChunk + 0x08));
        Assert.Equal(ZdcFormat.AacsRequiredOne, ZdcFixture.ReadUInt32(bytes, dataChunk + 0x0C));
        Assert.Equal(ZdcFormat.AacsRequiredMagic2, ZdcFixture.ReadUInt32(bytes, dataChunk + 0x10));
        Assert.Equal(ZdcFormat.AacsCompressionField, ZdcFixture.ReadUInt32(bytes, dataChunk + 0x14));
        Assert.All(bytes[(dataChunk + 0x18)..(dataChunk + 0x80)], b => Assert.Equal(0, b));

        var compressed = ZdcFixture.ReadUInt32(bytes, dataChunk + 0x80);
        var uncompressed = ZdcFixture.ReadUInt32(bytes, dataChunk + 0x84);
        var wrapperSize = ZdcFixture.ReadUInt32(bytes, dataChunk + 0x8C);
        Assert.Equal(wrapperSize, (uint)ZdcFormat.AacsHeaderSize + compressed);
        Assert.True(uncompressed > 0);

        var record = ZdcFixture.ChunkOffset(ZdcFormat.FirstDirectoryChunk) + ZdcFormat.ChunkHeaderSize;
        var storedLength = (int)((ulong)ZdcFixture.ReadUInt32(bytes, record + 0x04) | ((ulong)ZdcFixture.ReadUInt32(bytes, record + 0x08) << 32));
        Assert.Equal(wrapperSize, (uint)storedLength);
    }

    [Fact]
    public void RoundTrip_CrossChunkPayload_RestoresEveryByte()
    {
        var payload = new byte[100_000];
        new Random(490).NextBytes(payload);
        var database = new ZdcDatabase();
        database.Members.Add(new ZdcMember { FileName = "big.bin", Payload = payload });

        var bytes = ZdcFixture.Serialize(database);
        using var stream = ZdcFixture.StreamOf(bytes);
        var restored = ZdcReader.Read(stream);

        var member = Assert.Single(restored.Members, m => m.FileName == "zdc_index.xml");
        Assert.Empty(member.Payload);
        var big = Assert.Single(restored.Members, m => m.FileName == "big.bin");
        Assert.Equal(payload, big.Payload);
        Assert.True(big.DataHeaders.Count > 1, "payload must span several file-data chunks.");
    }

    [Fact]
    public void RoundTrip_MultiChunkPayload_UsesChainedIndexChunks()
    {
        var payload = new byte[4_300_000];
        new Random(496).NextBytes(payload);
        var database = new ZdcDatabase();
        database.Members.Add(new ZdcMember { FileName = "huge.bin", Payload = payload });

        var bytes = ZdcFixture.Serialize(database);
        using var stream = ZdcFixture.StreamOf(bytes);
        var restored = ZdcReader.Read(stream);

        var huge = Assert.Single(restored.Members, m => m.FileName == "huge.bin");
        Assert.Equal(payload, huge.Payload);
        Assert.True(huge.IndexReserved.Count > 1, "data references must chain across index chunks.");
    }

    [Fact]
    public void RoundTrip_NineMembers_SpansTwoDirectoryChunks()
    {
        var bytes = ZdcFixture.Serialize(ZdcFixture.MultiChunkDatabase());
        using var stream = ZdcFixture.StreamOf(bytes);
        var restored = ZdcReader.Read(stream);

        Assert.Equal(2, restored.TreeReserved.Count);
        Assert.Equal(10, restored.Members.Count);
        Assert.Equal(bytes, ZdcFixture.Serialize(restored));
    }

    [Fact]
    public void RoundTrip_EmptyMember_PreservedAsEmpty()
    {
        var bytes = ZdcFixture.Serialize(ZdcFixture.EmptyPayloadDatabase());
        var record = ZdcFixture.ChunkOffset(ZdcFormat.FirstDirectoryChunk) + ZdcFormat.ChunkHeaderSize + ZdcFormat.MemberRecordSize;

        // zdc_index.xml sorts first; the empty member is the second record with no chain.
        Assert.Equal(-1, ZdcFixture.ReadInt32(bytes, record));
        Assert.Equal(0u, ZdcFixture.ReadUInt32(bytes, record + 0x04));
        Assert.Equal(0u, ZdcFixture.ReadUInt32(bytes, record + 0x08));

        using var stream = ZdcFixture.StreamOf(bytes);
        var restored = ZdcReader.Read(stream);
        var empty = Assert.Single(restored.Members, m => m.FileName == "empty.xml");
        Assert.Empty(empty.Payload);
        Assert.Empty(empty.DataHeaders);
    }

    [Fact]
    public void Serialize_SameDatabaseTwice_IsByteIdentical()
    {
        var first = ZdcFixture.Serialize(ZdcFixture.SampleDatabase());
        var second = ZdcFixture.Serialize(ZdcFixture.SampleDatabase());
        Assert.Equal(first, second);
    }

    [Fact]
    public void RoundTrip_UnchangedCatalog_RewritesByteIdentical()
    {
        var bytes = ZdcFixture.Serialize(ZdcFixture.SampleDatabase());
        using var stream = ZdcFixture.StreamOf(bytes);
        var restored = ZdcReader.Read(stream);
        Assert.Equal(bytes, ZdcFixture.Serialize(restored));
    }
}
