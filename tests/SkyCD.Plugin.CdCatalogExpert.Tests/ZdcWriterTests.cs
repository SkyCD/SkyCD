using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using Xunit;

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
            FormatId = "cdcatalogexpert-zdc",
            Payload = rows
        });
        Assert.True(write.Success, write.Error);

        target.Position = 0;
        var restored = ZdcReader.Read(target);
        Assert.Contains(restored.Members, m => m.FileName == "disc.xml");
        Assert.Contains(restored.Members, m => m.FileName == ZdcFormat.IndexMemberName);
    }

    [Fact]
    public async Task RoundTrip_PluginReadWrite_PreservesRowsAndRewritesIdentical()
    {
        using var source = ZdcFixture.StreamOf(ZdcFixture.Serialize(ZdcFixture.SampleDatabase()));
        var read = await plugin.ReadAsync(ZdcFixture.ReadRequest(source));
        Assert.True(read.Success);
        var document = Assert.IsType<ZdcReportDocument>(read.Payload);

        using var target = new MemoryStream();
        var write = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = target,
            FormatId = "cdcatalogexpert-zdc",
            Payload = document
        });
        Assert.True(write.Success, write.Error);
        Assert.Equal(((MemoryStream)source).ToArray(), target.ToArray());

        target.Position = 0;
        var reread = await plugin.ReadAsync(ZdcFixture.ReadRequest(target));
        Assert.True(reread.Success);
        var redocument = Assert.IsType<ZdcReportDocument>(reread.Payload);
        Assert.Equal(document.Rows.Count, redocument.Rows.Count);
        for (var i = 0; i < document.Rows.Count; i++)
        {
            Assert.Equal(document.Rows[i]["nodeId"], redocument.Rows[i]["nodeId"]);
            Assert.Equal(document.Rows[i]["name"], redocument.Rows[i]["name"]);
        }
    }

    [Fact]
    public async Task WriteAsync_InvalidPayload_ReturnsFailure()
    {
        using var target = new MemoryStream();
        var write = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = target,
            FormatId = "cdcatalogexpert-zdc",
            Payload = 42
        });
        Assert.False(write.Success);
        Assert.NotEmpty(write.Error ?? string.Empty);
    }
}
