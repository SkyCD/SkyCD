using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using Xunit;

namespace SkyCD.Plugin.FileFormat.DiskBase.Tests;

public sealed class DiskBaseReaderTests
{
    [Fact]
    public async Task DetectionAndReadRejectUnrelatedUnsupportedAndTruncatedData()
    {
        var plugin = new DiskBasePlugin();
        var fixture = DiskBaseFixture.Create();
        var unrelated = (byte[])fixture.Bytes.Clone();
        unrelated[0] = 0;
        var unsupported = (byte[])fixture.Bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(unsupported.AsSpan(18), 0x0500);
        foreach (var bytes in new[] { unrelated, unsupported, fixture.Bytes[..100], fixture.Bytes[..(fixture.TreeOffset + 20)] })
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var result = await plugin.ReadAsync(Request(stream));
            Assert.False(result.Success);
            Assert.NotNull(result.Error);
        }

        using var staleTail = new MemoryStream(fixture.Bytes[..^1], writable: false);
        Assert.True((await plugin.ReadAsync(Request(staleTail))).Success);

        using var invalid = new MemoryStream(unrelated);
        Assert.False(plugin.CanHandleContent(invalid, "x.qdr"));
        using var valid = new MemoryStream(fixture.Bytes);
        Assert.False(plugin.CanHandleContent(valid, "x.txt"));
        Assert.True(plugin.CanHandleContent(valid));
    }

    [Fact]
    public async Task RejectsCorruptPointersLengthsRecordTypesAndOldTreeLayouts()
    {
        var fixture = DiskBaseFixture.Create();
        var badKey = (byte[])fixture.Bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(badKey.AsSpan(DiskBaseFixture.KeyOffset + 26), uint.MaxValue);
        var badList = (byte[])fixture.Bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(badList.AsSpan(fixture.TreeOffset + 9 + 1180 + 4 + 19), uint.MaxValue);
        var badMarker = (byte[])fixture.Bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(badMarker.AsSpan(fixture.RootListOffset + 4), 0);
        var badType = (byte[])fixture.Bytes.Clone();
        badType[fixture.DescriptionOffset + 8] = 1;
        var oldTree = (byte[])fixture.Bytes.Clone();
        var treeSize = BinaryPrimitives.ReadUInt32LittleEndian(oldTree.AsSpan(fixture.TreeOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(oldTree.AsSpan(fixture.TreeOffset + 4), ~treeSize);
        var badChildren = (byte[])fixture.Bytes.Clone();
        BinaryPrimitives.WriteInt16LittleEndian(badChildren.AsSpan(fixture.TreeOffset + 9 + 1180 + 2), 9);
        var duplicateTree = (byte[])fixture.Bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(duplicateTree.AsSpan(DiskBaseFixture.KeyOffset + 32), (uint)fixture.TreeOffset);
        var badCount = (byte[])fixture.Bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(badCount.AsSpan(fixture.RootListOffset + 9), int.MaxValue);
        var negativeDirectoryCount = (byte[])fixture.Bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(negativeDirectoryCount.AsSpan(fixture.TreeOffset + 9 + 1180 + 4 + 23), -1);
        var badDescription = (byte[])fixture.Bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(badDescription.AsSpan(fixture.TreeOffset + 9 + 1180 + 4), uint.MaxValue);
        var badDate = (byte[])fixture.Bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(badDate.AsSpan(fixture.TreeOffset + 9 + 4), uint.MaxValue);
        var badString = (byte[])fixture.Bytes.Clone();
        badString[fixture.TreeOffset + 9 + 64] = 32;
        foreach (var bytes in new[] { badKey, badList, badMarker, badType, oldTree, badChildren, duplicateTree,
                     badCount, negativeDirectoryCount, badDescription, badDate, badString })
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var result = await new DiskBasePlugin().ReadAsync(Request(stream));
            Assert.False(result.Success);
            Assert.Contains("DiskBase", result.Error);
        }
    }

    [Fact]
    public async Task CodePageCanBeSelectedAndIsNotAssumedToBeUtf8()
    {
        var fixture = DiskBaseFixture.Create();
        fixture.Bytes[fixture.TreeOffset + 9 + 116 + 1] = 0xE8;
        using var centralEuropean = new MemoryStream(fixture.Bytes, writable: false);
        var result = await new DiskBasePlugin(1250).ReadAsync(Request(centralEuropean));
        Assert.True(result.Success, result.Error);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => ((string)row["name"]!).StartsWith("č", StringComparison.Ordinal));
        using var westernEuropean = new MemoryStream(fixture.Bytes, writable: false);
        var other = await new DiskBasePlugin().ReadAsync(Request(westernEuropean));
        Assert.True(other.Success, other.Error);
        var otherRows = Assert.IsAssignableFrom<IReadOnlyList<Dictionary<string, object?>>>(other.Payload);
        Assert.Contains(otherRows, row => ((string)row["name"]!).StartsWith("è", StringComparison.Ordinal));

        fixture.Bytes[fixture.TreeOffset + 9 + 116 + 1] = 0xFF;
        using var invalidUtf8 = new MemoryStream(fixture.Bytes, writable: false);
        var invalid = await new DiskBasePlugin(65001).ReadAsync(Request(invalidUtf8));
        Assert.False(invalid.Success);
        Assert.Contains("code page", invalid.Error);
    }

    [Fact]
    public async Task CancellationAndWritingAreHandled()
    {
        using var source = new MemoryStream(DiskBaseFixture.Create().Bytes);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DiskBasePlugin().ReadAsync(Request(source), cancellation.Token));
        var result = await new DiskBasePlugin().WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "diskbase-qdr",
            Target = new MemoryStream(),
            Payload = new object()
        });
        Assert.False(result.Success);
        Assert.Contains("read-only", result.Error);
    }

    [Fact]
    public async Task CancellationDuringTreeImportIsPropagated()
    {
        var fixture = DiskBaseFixture.Create();
        using var cancellation = new CancellationTokenSource();
        using var source = new CancelOnTreeSeekStream(fixture.Bytes, fixture.TreeOffset, cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DiskBasePlugin().ReadAsync(Request(source), cancellation.Token));
    }

    private static FileFormatReadRequest Request(Stream stream) => new() { Source = stream, FormatId = "diskbase-qdr" };

    private sealed class CancelOnTreeSeekStream(byte[] bytes, int treeOffset, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override long Position
        {
            get => base.Position;
            set
            {
                base.Position = value;
                if (value == treeOffset)
                {
                    cancellation.Cancel();
                }
            }
        }
    }
}
