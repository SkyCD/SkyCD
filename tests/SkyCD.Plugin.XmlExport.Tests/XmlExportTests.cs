using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.XmlExport;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class XmlExportTests
{
    private const string CatalogXsd = """
        <?xml version="1.0" encoding="utf-8"?>
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" elementFormDefault="qualified">
          <xs:element name="Broken_X_Database">
            <xs:complexType>
              <xs:choice minOccurs="0" maxOccurs="unbounded">
                <xs:element name="Disk" type="containerType"/>
                <xs:element name="Folder" type="containerType"/>
                <xs:element name="File" type="fileType"/>
                <xs:element name="Item" type="containerType"/>
              </xs:choice>
              <xs:attribute name="Name" type="xs:string" use="required"/>
            </xs:complexType>
          </xs:element>
          <xs:complexType name="containerType">
            <xs:choice minOccurs="0" maxOccurs="unbounded">
              <xs:element name="Disk" type="containerType"/>
              <xs:element name="Folder" type="containerType"/>
              <xs:element name="File" type="fileType"/>
              <xs:element name="Item" type="containerType"/>
              <xs:element name="Metadata" type="metadataType"/>
            </xs:choice>
            <xs:attribute name="Name" type="xs:string" use="required"/>
            <xs:attribute name="Size" type="xs:nonNegativeInteger" use="required"/>
            <xs:attribute name="Date" type="xs:dateTime" use="optional"/>
          </xs:complexType>
          <xs:complexType name="fileType">
            <xs:choice minOccurs="0" maxOccurs="unbounded">
              <xs:element name="Metadata" type="metadataType"/>
            </xs:choice>
            <xs:attribute name="Name" type="xs:string" use="required"/>
            <xs:attribute name="Size" type="xs:nonNegativeInteger" use="required"/>
            <xs:attribute name="Date" type="xs:dateTime" use="optional"/>
          </xs:complexType>
          <xs:complexType name="metadataType">
            <xs:attribute name="Name" type="xs:string" use="required"/>
            <xs:attribute name="Value" type="xs:string" use="required"/>
          </xs:complexType>
        </xs:schema>
        """;

    private const string ListXsd = """
        <?xml version="1.0" encoding="utf-8"?>
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" elementFormDefault="qualified">
          <xs:element name="ItemList">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="Item" type="itemType" minOccurs="0" maxOccurs="unbounded"/>
              </xs:sequence>
              <xs:attribute name="Database" type="xs:string" use="required"/>
            </xs:complexType>
          </xs:element>
          <xs:complexType name="itemType">
            <xs:sequence>
              <xs:any minOccurs="0" maxOccurs="unbounded" processContents="skip"/>
            </xs:sequence>
            <xs:attribute name="Type" type="xs:string" use="required"/>
          </xs:complexType>
        </xs:schema>
        """;

    [Fact]
    public void SaveFormats_ExposeBothXmlProfiles_AsExportOnly()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.DoesNotContain(openFormats, format => format.FormatId == "skycd-xml-catalog");
        Assert.DoesNotContain(openFormats, format => format.FormatId == "skycd-xml-list");
        Assert.Contains(saveFormats, format =>
            format.FormatId == "skycd-xml-catalog" && format.Extensions.Contains(".xml"));
        Assert.Contains(saveFormats, format =>
            format.FormatId == "skycd-xml-list" && format.Extensions.Contains(".xml"));
    }

    [Fact]
    public async Task ReadAsync_IsBlocked_ForBothProfiles()
    {
        var service = CreateService();
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("<ItemList/>"));

        await Assert.ThrowsAsync<FileFormatNotReadableException>(() =>
            service.ReadAsync(new FileFormatReadRequest
            {
                FormatId = "skycd-xml-catalog",
                Source = source
            }));

        await Assert.ThrowsAsync<FileFormatNotReadableException>(() =>
            service.ReadAsync(new FileFormatReadRequest
            {
                FormatId = "skycd-xml-list",
                Source = source
            }));
    }

    [Fact]
    public async Task Catalog_WriteAsync_NestedGolden_MatchesIssueContract()
    {
        var service = CreateService();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("nodeId", "1"), ("parentId", ""), ("kind", "disk"), ("name", "Media"),
                ("size", "123"), ("date", "2026-11-13T10:15:30Z")),
            Row(("nodeId", "2"), ("parentId", "1"), ("kind", "folder"), ("name", "Docs"),
                ("size", "123"), ("date", "2026-11-13T10:15:30Z")),
            Row(("nodeId", "3"), ("parentId", "2"), ("kind", "file"), ("name", "readme.txt"),
                ("size", "123"), ("date", "2026-11-13T10:15:30Z"))
        };

        var xml = await WriteAsync(service, "skycd-xml-catalog",
            new XmlExportRequest { Rows = payload, DatabaseName = "Catalog name" });

        Assert.Equal(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Broken_X_Database Name="Catalog name">
              <Disk Name="Media" Size="123" Date="2026-11-13T10:15:30Z">
                <Folder Name="Docs" Size="123" Date="2026-11-13T10:15:30Z">
                  <File Name="readme.txt" Size="123" Date="2026-11-13T10:15:30Z" />
                </Folder>
              </Disk>
            </Broken_X_Database>
            """.Replace("\r\n", "\n"),
            xml.Replace("\r\n", "\n"));
        Assert.Empty(ValidateAgainstSchema(xml, CatalogXsd));
    }

    [Fact]
    public async Task Catalog_WriteAsync_EscapesText_AndPreservesUnicode()
    {
        var service = CreateService();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("nodeId", "1"), ("parentId", ""), ("kind", "file"),
                ("name", "a & b <c> \"d\" 'e' \U0001F600 \u4E2D\u6587"),
                ("size", "42"),
                ("comment", "x > y & \"z\""))
        };

        var xml = await WriteAsync(service, "skycd-xml-catalog",
            new XmlExportRequest { Rows = payload, DatabaseName = "T & T" });

        Assert.Contains("Name=\"T &amp; T\"", xml);
        Assert.Contains("a &amp; b &lt;c&gt; &quot;d&quot; 'e' \U0001F600 \u4E2D\u6587", xml);
        Assert.Contains("<Metadata Name=\"comment\" Value=\"x &gt; y &amp; &quot;z&quot;\" />", xml);
        Assert.Empty(ValidateAgainstSchema(xml, CatalogXsd));
    }

    [Fact]
    public async Task Catalog_WriteAsync_PreservesInputOrder_AndKeepsOrphansAsRoots()
    {
        var service = CreateService();
        var payload = new List<Dictionary<string, object?>>
        {
            // Child listed before its parent; orphan references a missing parent.
            Row(("nodeId", "2"), ("parentId", "1"), ("kind", "file"), ("name", "late.txt")),
            Row(("nodeId", "9"), ("parentId", "missing"), ("kind", "file"), ("name", "orphan.txt")),
            Row(("nodeId", "1"), ("parentId", ""), ("kind", "disk"), ("name", "Media"))
        };

        var xml = await WriteAsync(service, "skycd-xml-catalog",
            new XmlExportRequest { Rows = payload });

        var orphanIndex = xml.IndexOf("orphan.txt", StringComparison.Ordinal);
        var diskIndex = xml.IndexOf("<Disk", StringComparison.Ordinal);
        var fileIndex = xml.IndexOf("late.txt", StringComparison.Ordinal);
        Assert.True(orphanIndex >= 0 && orphanIndex < diskIndex);
        Assert.True(fileIndex > diskIndex);
        Assert.Empty(ValidateAgainstSchema(xml, CatalogXsd));
    }

    [Fact]
    public async Task Catalog_WriteAsync_EmptyRows_WritesEmptyDatabase()
    {
        var service = CreateService();
        var payload = new List<Dictionary<string, object?>>();

        var xml = await WriteAsync(service, "skycd-xml-catalog",
            new XmlExportRequest { Rows = payload });

        Assert.Contains("<Broken_X_Database Name=\"Catalog\" />", xml.Replace("\r\n", "\n"));
        Assert.Empty(ValidateAgainstSchema(xml, CatalogXsd));
    }

    [Fact]
    public async Task Catalog_WriteAsync_MetadataRichRow_EmitsMetadataElements()
    {
        var service = CreateService();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("nodeId", "1"), ("parentId", ""), ("kind", "file"), ("name", "track.mp3"),
                ("size", "5000000000"), ("date", new DateTime(2026, 11, 13, 10, 15, 30, DateTimeKind.Utc)),
                ("author", "An Artist"), ("duration", 213), ("lossless", false))
        };

        var xml = await WriteAsync(service, "skycd-xml-catalog",
            new XmlExportRequest { Rows = payload });

        Assert.Contains("Size=\"5000000000\"", xml);
        Assert.Contains("Date=\"2026-11-13T10:15:30Z\"", xml);
        Assert.Contains("<Metadata Name=\"author\" Value=\"An Artist\" />", xml);
        Assert.Contains("<Metadata Name=\"duration\" Value=\"213\" />", xml);
        Assert.Contains("<Metadata Name=\"lossless\" Value=\"False\" />", xml);
        Assert.Empty(ValidateAgainstSchema(xml, CatalogXsd));
    }

    [Fact]
    public async Task Catalog_WriteAsync_UnknownKind_UsesNormalizedElement()
    {
        var service = CreateService();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("nodeId", "1"), ("parentId", ""), ("kind", "tape archive"), ("name", "Backup"))
        };

        var xml = await WriteAsync(service, "skycd-xml-catalog",
            new XmlExportRequest { Rows = payload });

        Assert.Contains("<tape_archive Name=\"Backup\"", xml);
    }

    [Fact]
    public async Task Catalog_WriteAsync_Stylesheet_EmitsProcessingInstruction()
    {
        var service = CreateService();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("kind", "file"), ("name", "a.txt"))
        };

        var xml = await WriteAsync(service, "skycd-xml-catalog",
            new XmlExportRequest { Rows = payload, Stylesheet = "pretty.xsl" });

        Assert.Contains("<?xml-stylesheet type=\"text/xsl\" href=\"pretty.xsl\"?>", xml);
    }

    [Theory]
    [InlineData("../evil.xsl")]
    [InlineData("sub/dir.xsl")]
    [InlineData("/abs/path.xsl")]
    [InlineData("C:\\temp\\style.xsl")]
    [InlineData("style.css")]
    [InlineData("..\\evil.xsl")]
    public async Task Catalog_WriteAsync_UnsafeStylesheet_FailsWithDiagnostics(string stylesheet)
    {
        var service = CreateService();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("kind", "file"), ("name", "a.txt"))
        };

        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            WriteAsync(service, "skycd-xml-catalog",
                new XmlExportRequest { Rows = payload, Stylesheet = stylesheet }));

        Assert.Contains("Stylesheet", error.Message);
    }

    [Fact]
    public async Task Catalog_WriteAsync_DatabaseName_DefaultsToOutputFileName()
    {
        var plugin = new XmlCatalogExportPlugin();
        var payload = new List<Dictionary<string, object?>>();

        await using var target = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-xml-catalog",
            Target = target,
            FileName = "my-export.xml",
            Payload = payload
        });

        Assert.True(result.Success);
        Assert.Contains("Name=\"my-export\"", Encoding.UTF8.GetString(target.ToArray()));
    }

    [Fact]
    public async Task List_WriteAsync_ColumnOrderGolden_MatchesIssueContract()
    {
        var service = CreateService();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("kind", "file"), ("name", "readme.txt"), ("size", "123"),
                ("modified", "2026-11-13T10:15:30Z"))
        };

        var xml = await WriteAsync(service, "skycd-xml-list",
            new XmlExportRequest
            {
                Rows = payload,
                DatabaseName = "Catalog name",
                Columns = ["Name", "Size", "Modified"]
            });

        Assert.Equal(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <ItemList Database="Catalog name">
              <Item Type="File">
                <Name>readme.txt</Name>
                <Size>123</Size>
                <Modified>2026-11-13T10:15:30Z</Modified>
              </Item>
            </ItemList>
            """.Replace("\r\n", "\n"),
            xml.Replace("\r\n", "\n"));
        Assert.Empty(ValidateAgainstSchema(xml, ListXsd));
    }

    [Fact]
    public async Task List_WriteAsync_BareRows_UseRowOrder_AndLabelNormalizedColumns()
    {
        var service = CreateService();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("nodeId", "7"), ("parentId", ""), ("kind", "folder"), ("name", "Docs"),
                ("File Size", "512"), ("description", "My <docs> & more"))
        };

        var xml = await WriteAsync(service, "skycd-xml-list",
            new XmlExportRequest { Rows = payload });

        Assert.Contains("<Item Type=\"Folder\">", xml);
        Assert.DoesNotContain("nodeId", xml);
        var nameIndex = xml.IndexOf("<Name>Docs</Name>", StringComparison.Ordinal);
        var sizeIndex = xml.IndexOf("<File_Size Label=\"File Size\">512</File_Size>", StringComparison.Ordinal);
        var descriptionIndex = xml.IndexOf(
            "<description>My &lt;docs&gt; &amp; more</description>", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0 && nameIndex < sizeIndex && sizeIndex < descriptionIndex);
        Assert.Empty(ValidateAgainstSchema(xml, ListXsd));
    }

    [Fact]
    public async Task List_WriteAsync_EmptyRows_WritesSelfClosingItemList()
    {
        var service = CreateService();
        var payload = new List<Dictionary<string, object?>>();

        var xml = await WriteAsync(service, "skycd-xml-list",
            new XmlExportRequest { Rows = payload });

        Assert.Contains("<ItemList Database=\"Catalog\" />", xml.Replace("\r\n", "\n"));
        Assert.Empty(ValidateAgainstSchema(xml, ListXsd));
    }

    [Fact]
    public async Task List_WriteAsync_UnknownColumn_WritesEmptyElement()
    {
        var service = CreateService();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("name", "a.txt"))
        };

        var xml = await WriteAsync(service, "skycd-xml-list",
            new XmlExportRequest { Rows = payload, Columns = ["Name", "Nope"] });

        Assert.Contains("<Name>a.txt</Name>", xml);
        Assert.Contains("<Nope />", xml);
    }

    [Fact]
    public async Task Catalog_WriteAsync_InvalidCharacter_FailsWithItemAndFieldDiagnostics()
    {
        var plugin = new XmlCatalogExportPlugin();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("kind", "file"), ("name", "good.txt"), ("comment", "bad \u0001 char"))
        };

        await using var target = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-xml-catalog",
            Target = target,
            Payload = payload
        });

        Assert.False(result.Success);
        Assert.Contains("good.txt", result.Error);
        Assert.Contains("comment", result.Error);
    }

    [Fact]
    public async Task List_WriteAsync_InvalidCharacter_FailsWithItemAndFieldDiagnostics()
    {
        var plugin = new XmlListExportPlugin();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("name", "good.txt"), ("note", "bad \u000B char"))
        };

        await using var target = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-xml-list",
            Target = target,
            Payload = payload
        });

        Assert.False(result.Success);
        Assert.Contains("good.txt", result.Error);
        Assert.Contains("note", result.Error);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("not-a-number")]
    [InlineData("12.5")]
    public async Task Catalog_WriteAsync_InvalidSize_FailsWithDiagnostics(string size)
    {
        var plugin = new XmlCatalogExportPlugin();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("kind", "file"), ("name", "sized.txt"), ("size", size))
        };

        await using var target = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-xml-catalog",
            Target = target,
            Payload = payload
        });

        Assert.False(result.Success);
        Assert.Contains("sized.txt", result.Error);
        Assert.Contains("Size", result.Error);
    }

    [Fact]
    public async Task Catalog_WriteAsync_InvalidDate_FailsWithDiagnostics()
    {
        var plugin = new XmlCatalogExportPlugin();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("kind", "file"), ("name", "dated.txt"), ("date", "yesterday-ish"))
        };

        await using var target = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-xml-catalog",
            Target = target,
            Payload = payload
        });

        Assert.False(result.Success);
        Assert.Contains("dated.txt", result.Error);
        Assert.Contains("Date", result.Error);
    }

    [Fact]
    public async Task Catalog_WriteAsync_MissingName_FailsWithDiagnostics()
    {
        var plugin = new XmlCatalogExportPlugin();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("kind", "file"), ("nodeId", "42"))
        };

        await using var target = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-xml-catalog",
            Target = target,
            Payload = payload
        });

        Assert.False(result.Success);
        Assert.Contains("Name", result.Error);
    }

    [Fact]
    public async Task Catalog_WriteAsync_CancelledToken_ThrowsOperationCanceled()
    {
        var service = CreateService();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("kind", "file"), ("name", "a.txt"))
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-xml-catalog",
                Target = new MemoryStream(),
                Payload = new XmlExportRequest { Rows = payload }
            }, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task List_WriteAsync_CancelledToken_ThrowsOperationCanceled()
    {
        var service = CreateService();
        var payload = new List<Dictionary<string, object?>>
        {
            Row(("name", "a.txt"))
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-xml-list",
                Target = new MemoryStream(),
                Payload = new XmlExportRequest { Rows = payload }
            }, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task List_WriteAsync_ReportsProgress_ReachingHundred()
    {
        var service = CreateService();
        var payload = new List<Dictionary<string, object?>> { Row(("name", "a.txt")) };
        var reported = new List<int>();
        var progress = new Progress<int>(reported.Add);

        await using var target = new MemoryStream();
        var result = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-xml-list",
            Target = target,
            Payload = new XmlExportRequest { Rows = payload },
            Progress = progress
        });

        Assert.True(result.Success);
        Assert.Contains(100, reported);
    }

    private static FileFormatManager CreateService()
    {
        return new FileFormatManager([new XmlCatalogExportPlugin(), new XmlListExportPlugin()]);
    }

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] entries)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
        {
            row[key] = value;
        }

        return row;
    }

    private static async Task<string> WriteAsync(FileFormatManager service, string formatId,
        XmlExportRequest request)
    {
        await using var target = new MemoryStream();
        var result = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = formatId,
            Target = target,
            Payload = request
        });

        Assert.True(result.Success);
        return Encoding.UTF8.GetString(target.ToArray());
    }

    private static IReadOnlyList<string> ValidateAgainstSchema(string xml, string xsd)
    {
        var schemas = new XmlSchemaSet();
        using (var schemaReader = XmlReader.Create(new StringReader(xsd)))
        {
            schemas.Add(null, schemaReader);
        }

        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemas,
            DtdProcessing = DtdProcessing.Prohibit
        };
        settings.ValidationEventHandler += (_, args) => errors.Add(args.Message);

        using var content = new StringReader(xml);
        using var reader = XmlReader.Create(content, settings);
        while (reader.Read())
        {
        }

        return errors;
    }
}
