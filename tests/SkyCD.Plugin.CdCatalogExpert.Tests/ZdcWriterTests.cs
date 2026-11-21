using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using Xunit;
using SkyCD.Plugin.CdCatalogExpert.Export;
using SkyCD.Plugin.CdCatalogExpert.Models;
using SkyCD.Plugin.CdCatalogExpert.Parsing;

namespace SkyCD.Plugin.CdCatalogExpert.Tests;

public sealed class ZdcWriterTests
{
    private readonly ZdcPlugin plugin = new();

    [Fact]
    public void OrderMembers_SortsIndexDiscsThenCovers()
    {
        var database = new ZdcDatabase();
        database.Members.Add(ZdcFixture.CoverMember(ZdcFixture.DiscGuidB, ZdcFixture.MinimalJpeg()));
        database.Members.Add(ZdcFixture.DiscMember(ZdcFixture.DiscGuidB, ZdcFixture.DiscXml(ZdcFixture.DiscGuidB)));
        database.Members.Add(ZdcFixture.DiscMember(ZdcFixture.DiscGuidA, ZdcFixture.DiscXml(ZdcFixture.DiscGuidA)));
        database.Members.Add(ZdcFixture.IndexMember(ZdcFixture.IndexXml(ZdcFixture.DiscGuidA)));

        var ordered = ZdcWriter.OrderMembers(database.Members).Select(m => m.FileName).ToList();
        Assert.Equal(
            [ZdcFormat.IndexMemberName, $"{ZdcFixture.DiscGuidA}.xml", $"{ZdcFixture.DiscGuidB}.xml", $"{ZdcFixture.DiscGuidB}.jpg"],
            ordered);
    }

    [Fact]
    public void Write_MissingIndex_SynthesizesEmptyIndex()
    {
        var database = new ZdcDatabase();
        database.Members.Add(ZdcFixture.DiscMember(ZdcFixture.DiscGuidA, ZdcFixture.DiscXml(ZdcFixture.DiscGuidA)));

        using var stream = ZdcFixture.StreamOf(ZdcFixture.Serialize(database));
        var restored = ZdcReader.Read(stream);
        Assert.Equal(2, restored.Members.Count);
        var index = Assert.Single(restored.Members, m => m.FileName == ZdcFormat.IndexMemberName);
        Assert.Empty(index.Payload);
    }

    [Fact]
    public void Write_NameTooLong_Throws()
    {
        var database = new ZdcDatabase();
        database.Members.Add(new ZdcMember { FileName = new string('a', 284) + ".xml", Payload = "<a/>"u8.ToArray() });
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(() => ZdcFixture.Serialize(database));
    }

    [Fact]
    public void Write_NonAsciiName_Throws()
    {
        var database = new ZdcDatabase();
        database.Members.Add(new ZdcMember { FileName = "crème.xml", Payload = "<a/>"u8.ToArray() });
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(() => ZdcFixture.Serialize(database));
    }

    [Fact]
    public void Write_EmptyName_Throws()
    {
        var database = new ZdcDatabase();
        database.Members.Add(new ZdcMember { FileName = string.Empty, Payload = "<a/>"u8.ToArray() });
        Assert.Throws<SkyCD.Plugin.CdCatalogExpert.Exceptions.ZdcCorruptException>(() => ZdcFixture.Serialize(database));
    }

    [Fact]
    public async Task WriteAsync_FreshRows_WritesNeutralDefaultsAndReadsBack()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = "media:fresh",
                ["parentId"] = null,
                ["kind"] = "media",
                ["name"] = "fresh",
                ["sizeBytes"] = "0"
            },
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = "file:fresh/disc.xml",
                ["parentId"] = "media:fresh",
                ["kind"] = "file",
                ["name"] = "disc.xml",
                ["sizeBytes"] = "0"
            }
        };

        using var target = new MemoryStream();
        var write = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = target,
            FormatId = ZdcFixture.FormatId,
            Payload = rows
        });
        Assert.True(write.Success, write.Error);

        target.Position = 0;
        var restored = ZdcReader.Read(target);
        Assert.Contains(restored.Members, m => m.FileName == ZdcFormat.IndexMemberName);
        var discMember = Assert.Single(restored.Members, m =>
            m.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
            m.FileName != ZdcFormat.IndexMemberName);
        Assert.Contains("disc.xml", Encoding.ASCII.GetString(discMember.Payload));
    }

    [Fact]
    public async Task RoundTrip_PluginReadWrite_PreservesHierarchyAndMetadata()
    {
        using var source = ZdcFixture.StreamOf(ZdcFixture.Serialize(ZdcFixture.SampleDatabase()));
        var read = await plugin.ReadAsync(ZdcFixture.ReadRequest(source));
        Assert.True(read.Success);
        var document = Assert.IsType<ZdcReportDocument>(read.Payload);

        using var target = new MemoryStream();
        var write = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = target,
            FormatId = ZdcFixture.FormatId,
            Payload = document,
            FileName = "catalog.zdc"
        });
        Assert.True(write.Success, write.Error);

        target.Position = 0;
        var reread = await plugin.ReadAsync(ZdcFixture.ReadRequest(target));
        Assert.True(reread.Success);
        var redocument = Assert.IsType<ZdcReportDocument>(reread.Payload);
        Assert.Equal(document.Rows.Count, redocument.Rows.Count);
        for (var i = 0; i < document.Rows.Count; i++)
        {
            Assert.Equal(document.Rows[i]["nodeId"], redocument.Rows[i]["nodeId"]);
            Assert.Equal(document.Rows[i]["parentId"], redocument.Rows[i]["parentId"]);
            Assert.Equal(document.Rows[i]["kind"], redocument.Rows[i]["kind"]);
            Assert.Equal(document.Rows[i]["name"], redocument.Rows[i]["name"]);
            Assert.Equal(document.Rows[i]["sizeBytes"], redocument.Rows[i]["sizeBytes"]);
            Assert.Equal(
                Properties(document.Rows[i]),
                Properties(redocument.Rows[i]));
        }
    }

    [Fact]
    public async Task WriteAsync_SameRowsTwice_IsByteIdentical()
    {
        using var source = ZdcFixture.StreamOf(ZdcFixture.Serialize(ZdcFixture.SampleDatabase()));
        var read = await plugin.ReadAsync(ZdcFixture.ReadRequest(source));
        var document = Assert.IsType<ZdcReportDocument>(read.Payload);

        using var first = new MemoryStream();
        using var second = new MemoryStream();
        Assert.True((await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = first,
            FormatId = ZdcFixture.FormatId,
            Payload = document
        })).Success);
        Assert.True((await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = second,
            FormatId = ZdcFixture.FormatId,
            Payload = document
        })).Success);
        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Fact]
    public async Task WriteAsync_NonRepresentableCharacter_ReturnsFailure()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = "media:1",
                ["parentId"] = null,
                ["kind"] = "media",
                ["name"] = "Disc \u4E2D",
                ["sizeBytes"] = "0"
            }
        };

        using var target = new MemoryStream();
        var write = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = target,
            FormatId = ZdcFixture.FormatId,
            Payload = rows
        });
        Assert.False(write.Success);
        Assert.Contains("Windows-1252", write.Error);
    }

    private static Dictionary<string, object?> Properties(Dictionary<string, object?> row) =>
        Assert.IsType<Dictionary<string, object?>>(row["properties"]);

    [Fact]
    public async Task WriteAsync_InvalidPayload_ReturnsFailure()
    {
        using var target = new MemoryStream();
        var write = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = target,
            FormatId = ZdcFixture.FormatId,
            Payload = 42
        });
        Assert.False(write.Success);
        Assert.NotEmpty(write.Error ?? string.Empty);
    }
}
