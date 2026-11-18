using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.CdCatalogExpert.Exceptions;
using Xunit;

namespace SkyCD.Plugin.CdCatalogExpert.Tests;

public sealed class ZdcCatalogXmlTests
{
    private const string GuidA = "{3f2504e0-4f89-11d3-9a0c-0305e82c3301}";

    [Fact]
    public void WriteIndex_EmitsWindows1252DeclarationAndRawValues()
    {
        var catalog = new ZdcCatalog { Name = "Caf\u00E9 Catalog" };
        catalog.Children.Add(new ZdcDisc
        {
            Name = "Disc",
            GuidText = GuidA,
            Barcode = "000123",
            Rating = "4",
            Capacity = "-1",
            FreeSpace = "9223372036854775807",
            ScanFlagA = "1",
            Keywords = "a, b"
        });

        var bytes = ZdcCatalogXmlWriter.WriteIndex(catalog);
        var text = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"windows-1252\"?>", text);
        Assert.Contains("N=\"Café Catalog\"", text);
        Assert.Contains("F=\"{3f2504e0-4f89-11d3-9a0c-0305e82c3301}\"", text);
        Assert.Contains("DB=\"000123\"", text);
        Assert.Contains("DR=\"4\"", text);
        Assert.Contains("K=\"-1\"", text);
        Assert.Contains("R=\"9223372036854775807\"", text);
        Assert.Contains("SO_A=\"1\"", text);
        Assert.Contains("W=\"a, b\"", text);
    }

    [Fact]
    public void WriteIndex_EscapesAttributesAndPreservesCrLf()
    {
        var catalog = new ZdcCatalog
        {
            Name = "Catalog & <name>",
            Description = "line 1\r\nline 2 & \"quoted\" <tag>"
        };

        var bytes = ZdcCatalogXmlWriter.WriteIndex(catalog);
        var text = Encoding.Latin1.GetString(bytes);
        Assert.Contains("&amp;", text);
        Assert.Contains("&lt;", text);
        Assert.Contains("&quot;", text);
        Assert.Contains("&#xD;&#xA;", text);

        var warnings = new List<string>();
        var parsed = ZdcCatalogXmlReader.ReadIndex("zdc_index.xml", bytes, warnings);
        Assert.Equal(catalog.Name, parsed.Name);
        Assert.Equal("line 1\r\nline 2 & \"quoted\" <tag>", parsed.Description);
    }

    [Fact]
    public void WriteIndex_NonRepresentableCharacter_ThrowsDedicatedException()
    {
        var catalog = new ZdcCatalog { Name = "Disc \u4E2D" };
        var exception = Assert.Throws<ZdcCharacterNotRepresentableException>(
            () => ZdcCatalogXmlWriter.WriteIndex(catalog));
        Assert.Equal("Z.N", exception.FieldName);
        Assert.Equal('\u4E2D', exception.Character);
    }

    [Fact]
    public void WriteIndex_ControlCharacter_ThrowsDedicatedException()
    {
        var catalog = new ZdcCatalog { Name = "Disc \u0001" };
        var exception = Assert.Throws<ZdcCharacterNotRepresentableException>(
            () => ZdcCatalogXmlWriter.WriteIndex(catalog));
        Assert.Equal('\u0001', exception.Character);
    }

    [Fact]
    public void ReadIndex_UnknownAttribute_IsRetainedAndRewritten()
    {
        var xml = $"<Z N=\"Catalog\" X=\"42\"><G N=\"Group\" Y=\"custom\"/></Z>";
        var warnings = new List<string>();
        var catalog = ZdcCatalogXmlReader.ReadIndex("zdc_index.xml", ZdcFixture.EncodeXml(xml), warnings);

        Assert.Equal("42", Assert.Single(catalog.ExtensionAttributes).Value);
        var group = Assert.IsType<ZdcGroup>(Assert.Single(catalog.Children));
        Assert.Equal("custom", Assert.Single(group.ExtensionAttributes).Value);

        var rewritten = Encoding.Latin1.GetString(ZdcCatalogXmlWriter.WriteIndex(catalog));
        Assert.Contains("X=\"42\"", rewritten);
        Assert.Contains("Y=\"custom\"", rewritten);
    }

    [Fact]
    public void ReadIndex_UnknownElement_WarnsAndContinues()
    {
        var xml = "<Z N=\"Catalog\"><G N=\"Group\"><Unknown N=\"x\"/></G><L N=\"Disc\" F=\"" + GuidA + "\"/></Z>";
        var warnings = new List<string>();
        var catalog = ZdcCatalogXmlReader.ReadIndex("zdc_index.xml", ZdcFixture.EncodeXml(xml), warnings);

        Assert.Contains(warnings, w => w.Contains("Unknown element 'Unknown'"));
        Assert.Equal(2, catalog.Children.Count);
        var group = Assert.IsType<ZdcGroup>(catalog.Children[0]);
        Assert.Equal("Group", group.Name);
        Assert.Empty(group.Children);
        Assert.IsType<ZdcDisc>(catalog.Children[1]);
    }

    [Fact]
    public void ReadIndex_InvalidNumber_Throws()
    {
        var xml = "<Z N=\"Catalog\"><L N=\"Disc\" F=\"" + GuidA + "\" T=\"not-a-number\"/></Z>";
        var exception = Assert.Throws<ZdcFormatException>(
            () => ZdcCatalogXmlReader.ReadIndex("zdc_index.xml", ZdcFixture.EncodeXml(xml), new List<string>()));
        Assert.Contains("L.T", exception.Message);
    }

    [Fact]
    public void ReadIndex_InvalidScanFlag_Throws()
    {
        var xml = "<Z N=\"Catalog\"><L N=\"Disc\" F=\"" + GuidA + "\" SO_A=\"2\"/></Z>";
        Assert.Throws<ZdcFormatException>(
            () => ZdcCatalogXmlReader.ReadIndex("zdc_index.xml", ZdcFixture.EncodeXml(xml), new List<string>()));
    }

    [Fact]
    public void ReadIndex_InvalidGuid_Throws()
    {
        var xml = "<Z N=\"Catalog\"><L N=\"Disc\" F=\"not-a-guid\"/></Z>";
        var exception = Assert.Throws<ZdcFormatException>(
            () => ZdcCatalogXmlReader.ReadIndex("zdc_index.xml", ZdcFixture.EncodeXml(xml), new List<string>()));
        Assert.Contains("not a valid GUID", exception.Message);
    }

    [Fact]
    public void ReadIndex_MissingRequiredName_Throws()
    {
        var xml = "<Z><L N=\"Disc\" F=\"" + GuidA + "\"/></Z>";
        Assert.Throws<ZdcFormatException>(
            () => ZdcCatalogXmlReader.ReadIndex("zdc_index.xml", ZdcFixture.EncodeXml(xml), new List<string>()));
    }

    [Fact]
    public void ReadIndex_Dtd_IsProhibited()
    {
        var xml = "<!DOCTYPE Z [<!ENTITY x \"y\">]><Z N=\"&x;\"/>";
        Assert.Throws<ZdcFormatException>(
            () => ZdcCatalogXmlReader.ReadIndex("zdc_index.xml", ZdcFixture.EncodeXml(xml), new List<string>()));
    }

    [Fact]
    public void ReadIndex_ExcessiveDepth_Throws()
    {
        var builder = new StringBuilder("<Z N=\"Catalog\">");
        for (var i = 0; i < ZdcFormat.MaxXmlDepth + 2; i++)
        {
            builder.Append("<G N=\"g\">");
        }

        for (var i = 0; i < ZdcFormat.MaxXmlDepth + 2; i++)
        {
            builder.Append("</G>");
        }

        builder.Append("</Z>");
        var exception = Assert.Throws<ZdcFormatException>(
            () => ZdcCatalogXmlReader.ReadIndex("zdc_index.xml", ZdcFixture.EncodeXml(builder.ToString()), new List<string>()));
        Assert.Contains("nesting exceeds", exception.Message);
    }

    [Fact]
    public void ReadIndex_Windows1252Text_Decodes()
    {
        var xml = "<Z N=\"Catalogue euro\" D=\"cr\u00E8me br\u00FBl\u00E9e \u00A3\"/>";
        var catalog = ZdcCatalogXmlReader.ReadIndex("zdc_index.xml", ZdcFixture.EncodeXml(xml), new List<string>());
        Assert.Equal("crème brûlée £", catalog.Description);
    }

    [Fact]
    public void ReadIndex_InvalidWindows1252Byte_Throws()
    {
        var bytes = ZdcFixture.EncodeXml("<Z N=\"Catalog\"/>");
        // 0x81 is one of the five undefined Windows-1252 positions.
        bytes[9] = 0x81;
        var exception = Assert.Throws<ZdcFormatException>(
            () => ZdcCatalogXmlReader.ReadIndex("zdc_index.xml", bytes, new List<string>()));
        Assert.Contains("not valid Windows-1252", exception.Message);
    }

    [Fact]
    public async Task ReadIndex_CancelledToken_ThrowsOperationCanceled()
    {
        var xml = "<Z N=\"Catalog\"><G N=\"Group\"/></Z>";
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Run(
            () => ZdcCatalogXmlReader.ReadIndex(
                "zdc_index.xml",
                ZdcFixture.EncodeXml(xml),
                new List<string>(),
                source.Token)));
    }

    [Fact]
    public void ReadDiscTree_EmptyDirectoriesAndLargeFiles_RoundTrip()
    {
        var tree = new ZdcDiscTree();
        tree.Entries.Add(new ZdcTreeEntry
        {
            IsDirectory = true,
            Name = "Nested",
            Attributes = "16",
            Children =
            {
                new ZdcTreeEntry { IsDirectory = true, Name = "Empty" },
                new ZdcTreeEntry { IsDirectory = false, Name = "big.bin", Size = ulong.MaxValue.ToString(), Attributes = "32" }
            }
        });

        var bytes = ZdcCatalogXmlWriter.WriteDiscTree(tree);
        var parsed = ZdcCatalogXmlReader.ReadDiscTree("disc.xml", bytes, new List<string>());

        var nested = Assert.Single(parsed.Entries);
        Assert.True(nested.IsDirectory);
        Assert.Equal(2, nested.Children.Count);
        Assert.True(nested.Children[0].IsDirectory);
        Assert.Empty(nested.Children[0].Children);
        Assert.Equal(ulong.MaxValue.ToString(), nested.Children[1].Size);
    }

    [Fact]
    public void ReadDiscTree_SizeOutsideUInt64_Throws()
    {
        var xml = "<R><F N=\"file\" S=\"18446744073709551616\"/></R>";
        var exception = Assert.Throws<ZdcFormatException>(
            () => ZdcCatalogXmlReader.ReadDiscTree("disc.xml", ZdcFixture.EncodeXml(xml), new List<string>()));
        Assert.Contains("unsigned size", exception.Message);
    }

    [Fact]
    public void ReadDiscTree_MissingSize_Throws()
    {
        var xml = "<R><F N=\"file\"/></R>";
        Assert.Throws<ZdcFormatException>(
            () => ZdcCatalogXmlReader.ReadDiscTree("disc.xml", ZdcFixture.EncodeXml(xml), new List<string>()));
    }

    [Fact]
    public void Read_MissingCover_WarnsAndKeepsDisc()
    {
        var database = new ZdcDatabase
        {
            Members =
            {
                ZdcFixture.IndexMember(
                    "<Z N=\"Catalog\"><L N=\"Disc\" F=\"" + GuidA + "\" G=\"missing.jpg\"/></Z>"),
                ZdcFixture.DiscMember("3f2504e0-4f89-11d3-9a0c-0305e82c3301", "<R/>")
            }
        };

        var result = ZdcCatalogParser.Read(database);
        var disc = Assert.IsType<ZdcDisc>(Assert.Single(result.Catalog.Children));
        Assert.Null(disc.Cover);
        Assert.Contains(result.Warnings, w => w.Contains("missing cover"));
    }

    [Fact]
    public void Read_DuplicateGuidReference_Throws()
    {
        var database = new ZdcDatabase
        {
            Members =
            {
                ZdcFixture.IndexMember(
                    "<Z N=\"Catalog\"><L N=\"One\" F=\"" + GuidA + "\"/><L N=\"Two\" F=\"" + GuidA + "\"/></Z>"),
                ZdcFixture.DiscMember("3f2504e0-4f89-11d3-9a0c-0305e82c3301", "<R/>")
            }
        };

        var exception = Assert.Throws<ZdcFormatException>(() => ZdcCatalogParser.Read(database));
        Assert.Contains("referenced more than once", exception.Message);
    }
}
