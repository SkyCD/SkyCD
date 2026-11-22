using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.AbeMeda.Native;
using SkyCD.Plugin.FileFormat.AbeMeda.Native.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;
using SkyCD.Plugin.FileFormat.AbeMeda.Native.Models;
using SkyCD.Plugin.FileFormat.AbeMeda.Native.Parsing;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Tests;

public sealed class AbeMedaNativeImportTests
{
    private const string MediaId = "media:demo";
    private const string ArtifactFolderId = "folder:demo/Embedded artifacts (unassociated)";
    private const string ArtifactIdPrefix = "file:demo/Embedded artifacts (unassociated)/";

    private static FileFormatManager CreateService()
    {
        return new FileFormatManager([new AbeMedaNativePlugin()]);
    }

    private static async Task<AbeMedaNativeReportDocument> ReadAsync(
        MemoryStream stream,
        string fileName = AbeMedaNativeFixture.DefaultFileName)
    {
        var result = await CreateService().ReadAsync(AbeMedaNativeFixture.ReadRequest(stream, fileName));
        Assert.True(result.Success, result.Error);
        return Assert.IsType<AbeMedaNativeReportDocument>(result.Payload);
    }

    private static async Task<FileFormatReadResult> ReadResultAsync(
        MemoryStream stream,
        string fileName = AbeMedaNativeFixture.DefaultFileName)
    {
        return await new AbeMedaNativePlugin().ReadAsync(AbeMedaNativeFixture.ReadRequest(stream, fileName));
    }

    private static Dictionary<string, Dictionary<string, object?>> IndexById(AbeMedaNativeReportDocument document)
    {
        return document.Rows.ToDictionary(
            row => Convert.ToString(row["id"], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            row => row,
            StringComparer.Ordinal);
    }

    private static string TextOf(Dictionary<string, object?> row, string key)
    {
        return Convert.ToString(row[key], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static Dictionary<string, object?> PropertiesOf(Dictionary<string, object?> row)
    {
        return Assert.IsType<Dictionary<string, object?>>(row["properties"]);
    }

    [Fact]
    public async Task ClassicCatalog_ImportsContainerAndArtifactRecords()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog();

        var document = await ReadAsync(stream);
        var byId = IndexById(document);

        Assert.Equal(5, document.Count);

        var media = byId[MediaId];
        Assert.Equal("media", TextOf(media, "type"));
        Assert.Null(media["parentId"]);
        Assert.Equal("demo", TextOf(media, "name"));

        var folder = byId[ArtifactFolderId];
        Assert.Equal("folder", TextOf(folder, "type"));
        Assert.Equal(MediaId, TextOf(folder, "parentId"));
        Assert.Equal(3L, Convert.ToInt64(folder["childrenCount"], System.Globalization.CultureInfo.InvariantCulture));

        var first = byId[$"{ArtifactIdPrefix}1"];
        Assert.Equal("file", TextOf(first, "type"));
        Assert.Equal(ArtifactFolderId, TextOf(first, "parentId"));
        Assert.Equal("Artifact 1", TextOf(first, "name"));

        var properties = PropertiesOf(first);
        Assert.Equal("JPEGGKON", TextOf(properties, "AbeMedaNativeHandler"));
        Assert.Equal("image/jpeg", TextOf(properties, "AbeMedaNativeMimeType"));
        Assert.Equal("true", TextOf(properties, "AbeMedaNativeRangesWithinFile"));
        Assert.Equal("0x00000100", TextOf(properties, "AbeMedaNativeRangeA"));
        Assert.Equal("0x00000200", TextOf(properties, "AbeMedaNativeRangeB"));

        Assert.Equal(105u, document.Summary.Version);
        Assert.Equal(105u, document.Summary.CompatibilityVersion);
        Assert.Equal("Classic", document.Summary.Generation);
        Assert.Equal(3u, document.Summary.DeclaredEntryCount);
        Assert.Equal(3, document.Summary.ImportedEntryCount);
        Assert.Equal(1, document.Summary.HandlerCounts["JPEGGKON"]);
        Assert.Equal(1, document.Summary.HandlerCounts["PNGfGKON"]);
        Assert.Equal(0, document.Summary.WarningCount);
    }

    [Fact]
    public async Task EmptyCatalog_ImportsSingleMediaNode()
    {
        using var stream = AbeMedaNativeFixture.CreateEmptyCatalog();

        var document = await ReadAsync(stream);

        Assert.Single(document);
        Assert.Equal(0L, Convert.ToInt64(document[0]["childrenCount"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0, document.Summary.ImportedEntryCount);
    }

    [Fact]
    public async Task UnknownHeaderFields_ArePreserved()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog =>
        {
            catalog.Unknown2C = 0xDEADBEEF;
            catalog.Unknown30 = 0x0BADF00D;
            catalog.Unknown34 = 7;
            catalog.Unknown38 = 11;
            catalog.Integrity = 0x11223344;
        });

        var document = await ReadAsync(stream);
        var properties = PropertiesOf(IndexById(document)[MediaId]);

        Assert.Equal("0xDEADBEEF", TextOf(properties, "AbeMedaNativeUnknown.0x2C"));
        Assert.Equal("0x0BADF00D", TextOf(properties, "AbeMedaNativeUnknown.0x30"));
        Assert.Equal("0x00000007", TextOf(properties, "AbeMedaNativeUnknown.0x34"));
        Assert.Equal("0x0000000B", TextOf(properties, "AbeMedaNativeUnknown.0x38"));
        Assert.Equal("0x11223344", TextOf(properties, "AbeMedaNativeIntegrity"));
    }

    [Fact]
    public async Task NonIncreasingOrdinals_AreSkippedWithWarning()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog =>
        {
            catalog.Entries.Clear();
            catalog.Entries.Add(new SyntheticEntry(1, 100, 0, "JPEGGKON", 0x10, 0x20));
            catalog.Entries.Add(new SyntheticEntry(1, 200, 0, "JPEGGKON", 0x30, 0x40));
            catalog.Entries.Add(new SyntheticEntry(3, 300, 0, "PNGfGKON", 0x50, 0x60));
        });

        var document = await ReadAsync(stream);
        var byId = IndexById(document);

        Assert.True(byId.ContainsKey($"{ArtifactIdPrefix}1"));
        Assert.True(byId.ContainsKey($"{ArtifactIdPrefix}3"));
        Assert.Equal(2, document.Summary.ImportedEntryCount);
        Assert.Equal(1, document.Summary.SkippedEntryCount);
        Assert.Contains(document.Summary.Warnings, warning => warning.Contains("non-increasing ordinal"));
    }

    [Fact]
    public async Task NonPrintableHandler_IsPreservedAsHexAndWarned()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog =>
        {
            catalog.Entries.Clear();
            catalog.Entries.Add(new SyntheticEntry(
                1, 100, 0, "ignored", 0x10, 0x20,
                RawHandler: [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]));
        });

        var document = await ReadAsync(stream);
        var properties = PropertiesOf(IndexById(document)[$"{ArtifactIdPrefix}1"]);

        Assert.Equal("0x0102030405060708", TextOf(properties, "AbeMedaNativeHandler"));
        Assert.Equal("false", TextOf(properties, "AbeMedaNativeHandlerIsPrintable"));
        Assert.Contains(document.Summary.Warnings, warning => warning.Contains("non-text handler"));
    }

    [Fact]
    public async Task OutOfFileRanges_AreWarnedButRetained()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog =>
        {
            catalog.Entries.Clear();
            catalog.Entries.Add(new SyntheticEntry(1, 100, 0, "JPEGGKON", uint.MaxValue, 0x10));
        });

        var document = await ReadAsync(stream);
        var properties = PropertiesOf(IndexById(document)[$"{ArtifactIdPrefix}1"]);

        Assert.Equal("false", TextOf(properties, "AbeMedaNativeRangesWithinFile"));
        Assert.Contains(document.Summary.Warnings, warning => warning.Contains("outside the file"));
        Assert.Equal(1, document.Summary.ImportedEntryCount);
    }

    [Fact]
    public async Task BlobValueOutsideFile_IsWarnedButRetained()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog =>
        {
            catalog.Entries.Clear();
            catalog.Entries.Add(new SyntheticEntry(1, ulong.MaxValue, 0, "JPEGGKON", 0x10, 0x20));
        });

        var document = await ReadAsync(stream);

        Assert.Equal(1, document.Summary.ImportedEntryCount);
        Assert.Contains(document.Summary.Warnings, warning => warning.Contains("blob value"));
    }

    [Fact]
    public async Task UnsupportedVersion_FailsWithActionableError()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog => catalog.Version = 106);

        var result = await ReadResultAsync(stream);

        Assert.False(result.Success);
        Assert.Contains("version", result.Error);
        Assert.Contains("106", result.Error);
    }

    [Fact]
    public async Task VersionMarkerMismatch_FailsWithActionableError()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog => catalog.CompatibilityVersion = 104);

        var result = await ReadResultAsync(stream);

        Assert.False(result.Success);
        Assert.Contains("compatibility marker", result.Error);
    }

    [Fact]
    public async Task UnsupportedHeaderSize_FailsWithActionableError()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog => catalog.HeaderSize = 0x80);

        var result = await ReadResultAsync(stream);

        Assert.False(result.Success);
        Assert.Contains("header size", result.Error);
    }

    [Fact]
    public async Task UnsupportedEntryStride_FailsWithActionableError()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog => catalog.EntryStride = 40);

        var result = await ReadResultAsync(stream);

        Assert.False(result.Success);
        Assert.Contains("stride", result.Error);
    }

    [Fact]
    public async Task DeclaredEntryTablePastSection_FailsWithActionableError()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog => catalog.DeclaredEntryCount = 100);

        var result = await ReadResultAsync(stream);

        Assert.False(result.Success);
        Assert.Contains("entry table", result.Error);
    }

    [Fact]
    public async Task SectionOutsideFile_FailsWithActionableError()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(
            catalog => catalog.HeaderSectionLengthOverride = uint.MaxValue);

        var result = await ReadResultAsync(stream);

        Assert.False(result.Success);
        Assert.Contains("section", result.Error);
    }

    [Fact]
    public async Task TruncatedHeader_FailsWithActionableError()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog => catalog.TruncateTo = 0x50);

        var result = await ReadResultAsync(stream);

        Assert.False(result.Success);
        Assert.Contains("header needs at least", result.Error);
    }

    [Fact]
    public async Task WrongSignature_FailsWithActionableError()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog => catalog.Magic = "XXXXXXXX");

        var result = await ReadResultAsync(stream);

        Assert.False(result.Success);
        Assert.Contains("signature", result.Error);
    }

    [Fact]
    public void HeaderReader_RejectsBadSentinelWithTypedError()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog(catalog => catalog.SentinelOverride = 0);

        var exception = Assert.Throws<AbeMedaNativeCorruptCatalogException>(
            () => AbeMedaNativeHeaderReader.Read(stream, CancellationToken.None));

        Assert.Contains("sentinel", exception.Message);
    }

    [Theory]
    [InlineData(".abeMeda7")]
    [InlineData(".neofinder7")]
    public async Task ExtendedGeneration_IsRecognizedButGated(string extension)
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog();

        var result = await ReadResultAsync(stream, "catalog" + extension);

        Assert.False(result.Success);
        Assert.Contains("not yet importable", result.Error);
    }

    [Fact]
    public async Task ReadAsync_PreCanceledToken_Throws()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new AbeMedaNativePlugin().ReadAsync(
                AbeMedaNativeFixture.ReadRequest(stream), cancellation.Token));
    }

    [Fact]
    public async Task WriteAsync_IsReadOnly()
    {
        var result = await new AbeMedaNativePlugin().WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "abemeda-native",
            Target = new System.IO.MemoryStream(),
            Payload = new object()
        });

        Assert.False(result.Success);
        Assert.Contains("read-only", result.Error);
    }

    [Fact]
    public async Task ReadAsync_ReportsProgressToCompletion()
    {
        using var stream = AbeMedaNativeFixture.CreateCatalog();
        var progress = new CapturingProgress();
        var request = AbeMedaNativeFixture.ReadRequest(stream);
        var result = await new AbeMedaNativePlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = request.FormatId,
            Source = request.Source,
            FileName = request.FileName,
            Progress = progress
        });

        Assert.True(result.Success, result.Error);
        Assert.Contains(100, progress.Values);
    }

    private sealed class CapturingProgress : IProgress<int>
    {
        public List<int> Values { get; } = [];

        public void Report(int value)
        {
            Values.Add(value);
        }
    }
}
