using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using SkyCD.Plugin.CdCatalogExpert.Models;
using SkyCD.Plugin.CdCatalogExpert.Parsing;

namespace SkyCD.Plugin.CdCatalogExpert.Tests;

public sealed class ZdcNegativeTests
{
    private readonly ZdcPlugin plugin = new();

    private static byte[] Sample() => ZdcFixture.Serialize(ZdcFixture.SampleDatabase());

    // Sample layout: tree chunk 4 (4 records), index chunks 5..8, data chunks 9..12.
    private static int TreeChunk => ZdcFixture.ChunkOffset(4);
    private static int RecordOffset(int slot) => TreeChunk + ZdcFormat.ChunkHeaderSize + (slot * ZdcFormat.MemberRecordSize);
    private static int DataChunkOffset(int chunk) => ZdcFixture.ChunkOffset(chunk) + ZdcFormat.ChunkHeaderSize;

    private static byte[] FixTreeChecksum(byte[] bytes)
    {
        var checksum = ZdcChecksums.XorWords(bytes, TreeChunk + ZdcFormat.ChunkHeaderSize, ZdcFormat.DataAreaSize);
        bytes[TreeChunk + 0x04] = (byte)(checksum & 0xFF);
        bytes[TreeChunk + 0x05] = (byte)((checksum >> 8) & 0xFF);
        bytes[TreeChunk + 0x06] = (byte)((checksum >> 16) & 0xFF);
        bytes[TreeChunk + 0x07] = (byte)((checksum >> 24) & 0xFF);
        return bytes;
    }

    private static byte[] FixDataChecksum(byte[] bytes, int chunk)
    {
        var area = ZdcFixture.ChunkOffset(chunk) + ZdcFormat.ChunkHeaderSize;
        var checksum = ZdcChecksums.XorWords(bytes, area, ZdcFormat.DataAreaSize);
        var at = ZdcFixture.ChunkOffset(chunk) + 0x04;
        bytes[at] = (byte)(checksum & 0xFF);
        bytes[at + 1] = (byte)((checksum >> 8) & 0xFF);
        bytes[at + 2] = (byte)((checksum >> 16) & 0xFF);
        bytes[at + 3] = (byte)((checksum >> 24) & 0xFF);
        return bytes;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(364)]
    [InlineData(4000)]
    [InlineData(-1)]
    public void Read_TruncatedInput_ThrowsCorrupt(int length)
    {
        var bytes = Sample();
        var cut = length < 0 ? bytes.Length - 1 : Math.Min(length, bytes.Length - 1);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes[..cut])));
    }

    [Fact]
    public void Read_BadContainerMagic_ThrowsCorrupt()
    {
        var bytes = ZdcFixture.Mutate(Sample(), copy => copy[0] = (byte)'B');
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_BadProfileMagic_ThrowsCorrupt()
    {
        var bytes = ZdcFixture.Mutate(Sample(), copy => copy[ZdcFormat.ProfileMagicOffset] = (byte)'X');
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_WrongChunkSize_ThrowsCorrupt()
    {
        var bytes = ZdcFixture.Mutate(Sample(), copy => copy[ZdcFormat.ChunkSizeOffset + 1] = 0x20);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_MisalignedLength_ThrowsCorrupt()
    {
        var bytes = Sample()[..^7];
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_DeclaredChunkCountMismatch_ThrowsCorrupt()
    {
        var bytes = ZdcFixture.Mutate(Sample(), copy => copy[ZdcFormat.ChunkCountOffset]++);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_LyingEntryCount_ThrowsCorrupt()
    {
        var bytes = ZdcFixture.Mutate(Sample(), copy => copy[ZdcFormat.EntryCountOffset] += 4);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_PrematureTreeTermination_ThrowsCorrupt()
    {
        var database = new ZdcDatabase();
        for (var i = 0; i < 7; i++)
        {
            database.Members.Add(ZdcFixture.DiscMember($"1111111{i}-0000-0000-0000-000000000000", ZdcFixture.DiscXml($"1111111{i}-0000-0000-0000-000000000000")));
        }

        var bytes = ZdcFixture.Mutate(ZdcFixture.Serialize(database), copy =>
        {
            var at = TreeChunk;
            copy[at] = 0xFF;
            copy[at + 1] = 0xFF;
            copy[at + 2] = 0xFF;
            copy[at + 3] = 0xFF;
        });
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_TreeCycle_ThrowsCorrupt()
    {
        var database = new ZdcDatabase();
        for (var i = 0; i < 7; i++)
        {
            database.Members.Add(ZdcFixture.DiscMember($"2222222{i}-0000-0000-0000-000000000000", ZdcFixture.DiscXml($"2222222{i}-0000-0000-0000-000000000000")));
        }

        var bytes = ZdcFixture.Mutate(ZdcFixture.Serialize(database), copy =>
        {
            var at = TreeChunk;
            copy[at] = 0x04;
            copy[at + 1] = 0x00;
            copy[at + 2] = 0x00;
            copy[at + 3] = 0x00;
        });
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_TreeChunkRevisited_ThrowsCorrupt()
    {
        var database = new ZdcDatabase();
        for (var i = 0; i < 14; i++)
        {
            var guid = $"333333{i:D2}-0000-0000-0000-0000000000{i:D2}";
            database.Members.Add(ZdcFixture.DiscMember(guid, ZdcFixture.DiscXml(guid)));
        }

        var bytes = ZdcFixture.Mutate(ZdcFixture.Serialize(database), copy =>
        {
            var second = ZdcFixture.ChunkOffset(5);
            copy[second] = 0x04;
            copy[second + 1] = 0x00;
            copy[second + 2] = 0x00;
            copy[second + 3] = 0x00;
        });
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_NonZeroUnusedTreeBytes_ThrowsCorrupt()
    {
        var bytes = Sample();
        bytes[TreeChunk + ZdcFormat.ChunkHeaderSize + (4 * ZdcFormat.MemberRecordSize)] = 0x01;
        FixTreeChecksum(bytes);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_BadTreeChecksum_ThrowsCorrupt()
    {
        var bytes = ZdcFixture.Mutate(Sample(), copy => copy[TreeChunk + ZdcFormat.ChunkHeaderSize] ^= 0xFF);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_MemberNameWithoutNul_ThrowsCorrupt()
    {
        var bytes = Sample();
        for (var i = 0; i < ZdcFormat.MemberNameFieldSize; i++)
        {
            bytes[RecordOffset(0) + 0x0E0 + i] = 0x41;
        }

        FixTreeChecksum(bytes);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_NonAsciiMemberName_ThrowsCorrupt()
    {
        var bytes = Sample();
        bytes[RecordOffset(1) + 0x0E0] = 0xC3;
        FixTreeChecksum(bytes);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_MemberNameWithSeparator_ThrowsCorrupt()
    {
        var database = new ZdcDatabase();
        database.Members.Add(new ZdcMember { FileName = "a/b.xml", Payload = Encoding.ASCII.GetBytes("<a/>") });
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(ZdcFixture.Serialize(database))));
    }

    [Fact]
    public void Read_DotDotMemberName_ThrowsCorrupt()
    {
        var database = new ZdcDatabase();
        database.Members.Add(new ZdcMember { FileName = "..", Payload = Encoding.ASCII.GetBytes("<a/>") });
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(ZdcFixture.Serialize(database))));
    }

    [Fact]
    public void Read_AbsoluteMemberName_ThrowsCorrupt()
    {
        var database = new ZdcDatabase();
        database.Members.Add(new ZdcMember { FileName = "C:disc.xml", Payload = Encoding.ASCII.GetBytes("<a/>") });
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(ZdcFixture.Serialize(database))));
    }

    [Fact]
    public void Read_DuplicateMemberNames_ThrowsCorrupt()
    {
        var database = new ZdcDatabase();
        database.Members.Add(ZdcFixture.DiscMember(ZdcFixture.DiscGuidA, ZdcFixture.DiscXml(ZdcFixture.DiscGuidA)));
        database.Members.Add(new ZdcMember { FileName = $"{ZdcFixture.DiscGuidA}".ToUpperInvariant() + ".XML", Payload = Encoding.ASCII.GetBytes("<a/>") });
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(ZdcFixture.Serialize(database))));
    }

    [Fact]
    public void Read_NonZeroMemberReserved_ThrowsCorrupt()
    {
        var bytes = Sample();
        bytes[RecordOffset(0) + 0x02C] = 0x01;
        FixTreeChecksum(bytes);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_StoredLengthOverflow_ThrowsCorruptBeforeAllocation()
    {
        var bytes = Sample();
        for (var i = 0; i < 8; i++)
        {
            bytes[RecordOffset(0) + 0x004 + i] = 0xFF;
        }

        FixTreeChecksum(bytes);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_ChainedDataWithZeroStoredLength_ThrowsCorrupt()
    {
        var bytes = Sample();
        for (var i = 0; i < 8; i++)
        {
            bytes[RecordOffset(0) + 0x004 + i] = 0x00;
        }

        FixTreeChecksum(bytes);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_MissingChainWithStoredData_ThrowsCorrupt()
    {
        var bytes = Sample();
        var at = RecordOffset(0);
        bytes[at] = 0xFF;
        bytes[at + 1] = 0xFF;
        bytes[at + 2] = 0xFF;
        bytes[at + 3] = 0xFF;
        FixTreeChecksum(bytes);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_BadDataChecksum_ThrowsCorrupt()
    {
        var bytes = ZdcFixture.Mutate(Sample(), copy => copy[DataChunkOffset(9)] ^= 0xFF);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_SharedIndexChunkAcrossMembers_ThrowsCorrupt()
    {
        var bytes = Sample();
        var at = RecordOffset(1);
        bytes[at] = 0x05;
        bytes[at + 1] = 0x00;
        bytes[at + 2] = 0x00;
        bytes[at + 3] = 0x00;
        FixTreeChecksum(bytes);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_IndexChunkOverlappingDirectoryTree_ThrowsCorrupt()
    {
        var bytes = Sample();
        var at = RecordOffset(1);
        bytes[at] = 0x04;
        bytes[at + 1] = 0x00;
        bytes[at + 2] = 0x00;
        bytes[at + 3] = 0x00;
        FixTreeChecksum(bytes);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_FileIndexReferenceOutOfRange_ThrowsCorrupt()
    {
        var bytes = Sample();
        var at = RecordOffset(0);
        bytes[at] = 0xFF;
        bytes[at + 1] = 0x7F;
        bytes[at + 2] = 0x00;
        bytes[at + 3] = 0x00;
        FixTreeChecksum(bytes);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_SharedDataChunkAcrossMembers_ThrowsCorrupt()
    {
        var bytes = Sample();
        // Point disc A's first data reference at the index member's data chunk 9.
        var indexChunk6 = ZdcFixture.ChunkOffset(6) + ZdcFormat.ChunkHeaderSize;
        bytes[indexChunk6] = 0x09;
        bytes[indexChunk6 + 1] = 0x00;
        bytes[indexChunk6 + 2] = 0x00;
        bytes[indexChunk6 + 3] = 0x00;
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_BadAacsMagic_ThrowsCorrupt()
    {
        var bytes = Sample();
        bytes[DataChunkOffset(9)] = (byte)'B';
        FixDataChecksum(bytes, 9);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Theory]
    [InlineData(0x08, 1u)]
    [InlineData(0x0C, 2u)]
    [InlineData(0x10, 0u)]
    [InlineData(0x14, 8u)]
    public void Read_BadAacsConstants_ThrowsCorrupt(int fieldOffset, uint value)
    {
        var bytes = Sample();
        var at = DataChunkOffset(9) + fieldOffset;
        bytes[at] = (byte)(value & 0xFF);
        bytes[at + 1] = (byte)((value >> 8) & 0xFF);
        bytes[at + 2] = (byte)((value >> 16) & 0xFF);
        bytes[at + 3] = (byte)((value >> 24) & 0xFF);
        FixDataChecksum(bytes, 9);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_CompressedLengthMismatch_ThrowsCorrupt()
    {
        var bytes = Sample();
        var at = DataChunkOffset(9) + 0x80;
        bytes[at]--;
        FixDataChecksum(bytes, 9);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_WrapperSizeMismatch_ThrowsCorrupt()
    {
        var bytes = Sample();
        var at = DataChunkOffset(9) + 0x8C;
        bytes[at]--;
        FixDataChecksum(bytes, 9);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_CorruptDeflateStream_ThrowsCorrupt()
    {
        var bytes = Sample();
        var at = DataChunkOffset(9) + ZdcFormat.AacsHeaderSize + 2;
        bytes[at] ^= 0xFF;
        FixDataChecksum(bytes, 9);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_CrcMismatch_ThrowsCorrupt()
    {
        var bytes = Sample();
        var at = DataChunkOffset(9) + 0x88;
        bytes[at] ^= 0xFF;
        FixDataChecksum(bytes, 9);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_NonZeroPaddingAfterStoredBytes_ThrowsCorrupt()
    {
        var bytes = Sample();
        bytes[DataChunkOffset(9) + 500] = 0x01;
        FixDataChecksum(bytes, 9);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_ExcessiveInflatedLength_ThrowsCorruptBeforeAllocation()
    {
        var bytes = Sample();
        var at = DataChunkOffset(9) + 0x84;
        bytes[at] = 0xFF;
        bytes[at + 1] = 0xFF;
        bytes[at + 2] = 0xFF;
        bytes[at + 3] = 0xFF;
        FixDataChecksum(bytes, 9);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_InvalidXmlPayload_ThrowsCorrupt()
    {
        var database = new ZdcDatabase();
        database.Members.Add(new ZdcMember { FileName = "broken.xml", Payload = Encoding.ASCII.GetBytes("not xml at all") });
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(ZdcFixture.Serialize(database))));
    }

    [Fact]
    public void Read_InvalidJpegPayload_ThrowsCorrupt()
    {
        var database = new ZdcDatabase();
        database.Members.Add(new ZdcMember { FileName = "broken.jpg", Payload = Encoding.ASCII.GetBytes("hello") });
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(ZdcFixture.Serialize(database))));
    }

    [Fact]
    public void Read_EncryptedDataFlag_ThrowsEncryptionError()
    {
        var bytes = Sample();
        var at = ZdcFixture.ChunkOffset(9) + 0x08;
        bytes[at] = 0x06;
        bytes[at + 1] = 0x01;
        bytes[at + 2] = 0x00;
        bytes[at + 3] = 0x00;
        FixDataChecksum(bytes, 9);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcEncryptionNotSupportedException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_UnsupportedDataFlags_ThrowsCorrupt()
    {
        var bytes = Sample();
        var at = ZdcFixture.ChunkOffset(9) + 0x08;
        bytes[at] = 0x07;
        FixDataChecksum(bytes, 9);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_NonZeroContainerKey_ThrowsEncryptionError()
    {
        var bytes = ZdcFixture.Mutate(Sample(), copy => copy[ZdcFormat.ContainerKeyOffset] = 0x01);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcEncryptionNotSupportedException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Read_NonZeroFileKey_ThrowsEncryptionError()
    {
        var bytes = Sample();
        bytes[RecordOffset(0) + 0x030] = 0x01;
        FixTreeChecksum(bytes);
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcEncryptionNotSupportedException>(
            () => ZdcReader.Read(ZdcFixture.StreamOf(bytes)));
    }

    [Fact]
    public void Unwrap_TrailingDeflateData_ThrowsCorrupt()
    {
        var payload = new byte[100];
        new Random(7).NextBytes(payload);
        var envelope = ZdcAacsCodec.Wrap(payload, 0);
        var at = ZdcFormat.AacsHeaderSize - 12;
        envelope[at] = 50;
        envelope[at + 1] = 0;
        envelope[at + 2] = 0;
        envelope[at + 3] = 0;
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcAacsCodec.Unwrap(envelope, "probe.xml"));
    }

    [Fact]
    public void Unwrap_TruncatedDeflateData_ThrowsCorrupt()
    {
        var payload = new byte[100];
        new Random(7).NextBytes(payload);
        var envelope = ZdcAacsCodec.Wrap(payload, 0);
        var at = ZdcFormat.AacsHeaderSize - 12;
        envelope[at] = 200;
        envelope[at + 1] = 0;
        envelope[at + 2] = 0;
        envelope[at + 3] = 0;
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(
            () => ZdcAacsCodec.Unwrap(envelope, "probe.xml"));
    }

    [Fact]
    public async Task ReadAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var stream = ZdcFixture.StreamOf(Sample());
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => plugin.ReadAsync(ZdcFixture.ReadRequest(stream), source.Token));
    }

    [Fact]
    public async Task ReadAsync_CorruptContainer_ReturnsFailure()
    {
        using var stream = ZdcFixture.StreamOf("not a catalog"u8.ToArray());
        var result = await plugin.ReadAsync(ZdcFixture.ReadRequest(stream));
        Assert.False(result.Success);
        Assert.NotEmpty(result.Error ?? string.Empty);
    }
}
