using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using SkyCD.Plugin.BrokenX.Export;
using SkyCD.Plugin.BrokenX.Models;
using SkyCD.Plugin.BrokenX.Parsing;

namespace SkyCD.Plugin.BrokenX.Tests;

public sealed class BxdWriterTests
{
    private readonly BxdPlugin plugin = new();

    [Fact]
    public void Write_EmptyDatabase_EmitsCanonicalBytes()
    {
        using var target = new MemoryStream();
        BxdWriter.Write(target, BxdFixture.EmptyDatabase());
        Assert.Equal(BxdFixture.CanonicalEmpty(), target.ToArray());
    }

    [Fact]
    public void Write_LargeBlob_UsesUint32Length()
    {
        using var target = new MemoryStream();
        BxdWriter.Write(target, BxdFixture.LargeBlobDatabase(70000));
        var bytes = target.ToArray();
        // Blob payloads always use a uint32 length: descriptor 0xC1 followed by 70000 = 0x11170.
        Assert.NotEqual(-1, IndexOf(bytes, [0xC1, 0x70, 0x11, 0x01, 0x00]));
    }

    [Fact]
    public void Write_LongName_UsesLongLengthForm()
    {
        var database = BxdFixture.EmptyDatabase();
        database.Roots.Add(new BxdItem { Flags = 0, Name = new string('n', 70000) });
        using var target = new MemoryStream();
        BxdWriter.Write(target, database);
        var bytes = target.ToArray();
        // Names of 65535 bytes or more use marker FF, 0xFFFF, then the uint32 length.
        Assert.NotEqual(-1, IndexOf(bytes, [0xFF, 0xFF, 0xFF, 0x70, 0x11, 0x01, 0x00]));
        var reread = BxdReader.Read(new MemoryStream(bytes), default);
        Assert.Equal(70000, reread.Roots[0].Name.Length);
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    [Theory]
    [MemberData(nameof(RoundTripDatabases))]
    public void RoundTrip_PreservesBytes(BxdDatabase database)
    {
        var original = BxdFixture.Serialize(database);
        var reread = BxdReader.Read(new MemoryStream(original), default);
        using var rewritten = new MemoryStream();
        BxdWriter.Write(rewritten, reread);
        Assert.Equal(original, rewritten.ToArray());
    }

    public static IEnumerable<object[]> RoundTripDatabases()
    {
        yield return new object[] { BxdFixture.EmptyDatabase() };
        yield return new object[] { BxdFixture.OneItemDatabase() };
        yield return new object[] { BxdFixture.SiblingDatabase() };
        yield return new object[] { BxdFixture.NestedDatabase(4) };
        yield return new object[] { BxdFixture.UnicodeDatabase() };
        yield return new object[] { BxdFixture.UnknownPropertyDatabase() };
        yield return new object[] { BxdFixture.LargeBlobDatabase(4096) };
    }

    [Fact]
    public async Task PluginWrite_ReadDocument_RoundTrips()
    {
        using var source = BxdFixture.StreamOf(BxdFixture.Serialize(BxdFixture.SiblingDatabase()));
        var read = await plugin.ReadAsync(BxdFixture.ReadRequest(source));
        Assert.True(read.Success);

        using var target = new MemoryStream();
        var write = await plugin.WriteAsync(new SkyCD.Plugin.Abstractions.Capabilities.FileFormats.FileFormatWriteRequest
        {
            FormatId = "brokenx-bxd",
            Target = target,
            Payload = read.Payload!
        });
        Assert.True(write.Success);

        using var reread = BxdFixture.StreamOf(target.ToArray());
        var second = await plugin.ReadAsync(BxdFixture.ReadRequest(reread));
        Assert.True(second.Success);
        var first = Assert.IsType<BxdReportDocument>(read.Payload);
        var again = Assert.IsType<BxdReportDocument>(second.Payload);
        Assert.Equal(first.Count, again.Count);
        Assert.Equal(
            first.Rows.Select(r => r["nodeId"]?.ToString()),
            again.Rows.Select(r => r["nodeId"]?.ToString()));
    }

    [Fact]
    public async Task PluginWrite_GenericRows_WritesFreshDatabase()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = "media:Disk",
                ["parentId"] = null,
                ["kind"] = "media",
                ["name"] = "Disk",
                ["sizeBytes"] = "0",
                ["properties"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            },
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = "file:Disk/a.txt",
                ["parentId"] = "media:Disk",
                ["kind"] = "file",
                ["name"] = "a.txt",
                ["sizeBytes"] = "0",
                ["properties"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            }
        };

        using var target = new MemoryStream();
        var write = await plugin.WriteAsync(new SkyCD.Plugin.Abstractions.Capabilities.FileFormats.FileFormatWriteRequest
        {
            FormatId = "brokenx-bxd",
            Target = target,
            Payload = rows
        });
        Assert.True(write.Success);

        using var reread = BxdFixture.StreamOf(target.ToArray());
        var second = await plugin.ReadAsync(BxdFixture.ReadRequest(reread));
        Assert.True(second.Success);
        var document = Assert.IsType<BxdReportDocument>(second.Payload);
        Assert.Contains(document.Rows, r => r["name"]?.ToString() == "a.txt");
    }
}
