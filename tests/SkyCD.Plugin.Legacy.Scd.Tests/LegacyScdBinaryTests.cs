using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Legacy.Scd;
using Xunit;

namespace SkyCD.LegacyFormats.Tests;

public sealed class LegacyScdBinaryTests
{
    [Fact]
    public async Task BinaryWriteAsync_UsesHistoricalLayoutAndIndependentDecoder()
    {
        var catalog = new LegacyScdCatalog();
        catalog.Entries.Add(new LegacyScdEntry { Path = @"[Disk]\Folder\Readme.txt", SizeBytes = 1536 });
        catalog.Entries.Add(new LegacyScdEntry { Path = @"[Disk]\Empty", IsDirectory = true });
        await using var stream = new MemoryStream();

        var result = await new LegacyScdBinaryPlugin().WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "legacy-scd-binary",
            Target = stream,
            Payload = catalog
        });

        Assert.True(result.Success, result.Error);
        var bytes = stream.ToArray();
        Assert.True(bytes.Length > 5519);
        Assert.Equal("# SkyCD File\r\n", System.Text.Encoding.ASCII.GetString(bytes, 0, 14));
        Assert.Equal(4d, BitConverter.ToDouble(bytes, 391));
        var nodes = DecodeHistoricalNodes(bytes);
        Assert.Equal(("", "n0000001", "[Disk]"), nodes[0]);
        Assert.Equal(("n0000001", "n0000002", "Folder"), nodes[1]);
        Assert.Equal(("n0000002", "n0000003", "[1.5        KB   ] Readme.txt"), nodes[2]);
        Assert.Equal(("n0000001", "n0000004", "Empty"), nodes[3]);
    }

    private static List<(string Parent, string Key, string Text)> DecodeHistoricalNodes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, 5519, bytes.Length - 5519);
        var ansi = System.Text.CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
        using var reader = new BinaryReader(stream, ansi);
        var nodes = new List<(string Parent, string Key, string Text)>();
        while (stream.Position < stream.Length)
        {
            nodes.Add((Read(), Read(), Read()));
        }

        return nodes;

        string Read() => ansi.GetString(reader.ReadBytes(reader.ReadUInt16()));
    }
    [Theory]
    [InlineData("binary-sample.scd")]
    [InlineData("text-sample.scd")]
    public async Task ReadAsync_PreservesTreeAndSizes(string name)
    {
        await using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Scd", name));
        var result = await new LegacyScdPlugin().ReadAsync(new FileFormatReadRequest { FormatId = "legacy-scd", Source = source });
        Assert.True(result.Success, result.Error);
        var entries = Assert.IsType<LegacyScdCatalog>(result.Payload).Entries;
        Assert.Equal(4, entries.Count);
        Assert.Equal("[Disk]", entries[0].Path);
        Assert.True(entries[0].IsDirectory);
        Assert.Equal(@"[Disk]\Folder [2023]", entries[1].Path);
        Assert.True(entries[1].IsDirectory);
        Assert.Equal(@"[Disk]\Folder [2023]\Readme.txt", entries[2].Path);
        Assert.Equal(1536L, entries[2].SizeBytes);
        Assert.False(entries[2].IsDirectory);
        Assert.Equal(@"[Disk]\Empty", entries[3].Path);
        Assert.True(entries[3].IsDirectory);
        Assert.True(source.CanRead);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(5518)]
    [InlineData(5520)]
    [InlineData(5540)]
    public async Task ReadAsync_RejectsTruncatedBinary(int length)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Scd", "binary-sample.scd"));
        await using var source = new MemoryStream(bytes[..length]);
        var result = await new LegacyScdPlugin().ReadAsync(new FileFormatReadRequest { FormatId = "legacy-scd", Source = source });
        Assert.False(result.Success);
        Assert.StartsWith("Invalid legacy binary SCD catalog:", result.Error);
        Assert.Null(result.Payload);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    [InlineData(1.5d)]
    [InlineData(double.MaxValue)]
    public async Task ReadAsync_RejectsInvalidNodeCount(double count)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Scd", "binary-sample.scd"));
        BitConverter.GetBytes(count).CopyTo(bytes, 391);
        await using var source = new MemoryStream(bytes);
        var result = await new LegacyScdPlugin().ReadAsync(new FileFormatReadRequest { FormatId = "legacy-scd", Source = source });
        Assert.False(result.Success);
        Assert.Contains("invalid node count", result.Error);
    }

    [Fact]
    public async Task ReadAsync_RejectsMissingParent()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Scd", "binary-sample.scd"));
        // The first node has an empty parent; the second refers to "root".
        bytes[5537] = (byte)'x';
        await using var source = new MemoryStream(bytes);
        var result = await new LegacyScdPlugin().ReadAsync(new FileFormatReadRequest { FormatId = "legacy-scd", Source = source });
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("cycle")]
    [InlineData("null")]
    public async Task ReadAsync_RejectsInvalidTrees(string problem)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Scd", "binary-sample.scd"));
        using var stream = new MemoryStream();
        stream.Write(bytes, 0, 5519);
        stream.Position = 391;
        stream.Write(BitConverter.GetBytes(2d));
        stream.Position = 5519;
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            var rows = problem switch
            {
                "duplicate" => new[] { ("", "root", "[Disk]"), ("root", "root", "Folder") },
                "cycle" => new[] { ("child", "root", "[Disk]"), ("root", "child", "Folder") },
                _ => new[] { ("", "root", "[Disk]"), ("root", "child", "Fol\0der") }
            };
            foreach (var (parent, key, text) in rows)
            {
                foreach (var value in new[] { parent, key, text })
                {
                    var data = System.Text.Encoding.Latin1.GetBytes(value);
                    writer.Write((ushort)data.Length);
                    writer.Write(data);
                }
            }
        }
        stream.Position = 0;
        var result = await new LegacyScdPlugin().ReadAsync(new FileFormatReadRequest { FormatId = "legacy-scd", Source = stream });
        Assert.False(result.Success);
        Assert.StartsWith("Invalid legacy binary SCD catalog:", result.Error);
    }
}
