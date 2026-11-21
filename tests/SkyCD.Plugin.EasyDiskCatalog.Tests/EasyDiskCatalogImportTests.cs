using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using Xunit;
using SkyCD.Plugin.EasyDiskCatalog.Models;

namespace SkyCD.Plugin.EasyDiskCatalog.Tests;

public sealed class EasyDiskCatalogImportTests : IDisposable
{
    private readonly string workDirectory =
        Path.Combine(Path.GetTempPath(), "edcdb-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(workDirectory))
            {
                Directory.Delete(workDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static Dictionary<string, Dictionary<string, object?>> IndexById(
        EasyDiskCatalogReportDocument document)
    {
        return document.Rows.ToDictionary(
            row => Convert.ToString(row["id"], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            row => row,
            StringComparer.Ordinal);
    }

    private static Dictionary<string, object?> PropertiesOf(Dictionary<string, object?> row)
    {
        return Assert.IsType<Dictionary<string, object?>>(row["properties"]);
    }

    private static string TextOf(Dictionary<string, object?> row, string key)
    {
        return Convert.ToString(row[key], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private async Task<EasyDiskCatalogReportDocument> ReadDirectAsync(string databasePath)
    {
        var plugin = new EasyDiskCatalogPlugin();
        await using var stream = File.OpenRead(databasePath);
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "edcdb",
            Source = stream,
            FileName = Path.GetFileName(databasePath),
            SourcePath = databasePath
        });
        Assert.True(result.Success, $"Import failed: {result.Error}");
        return Assert.IsType<EasyDiskCatalogReportDocument>(result.Payload);
    }

    [Fact]
    public async Task EncryptedFixture_OpensWithPluginAndImportsHierarchy()
    {
        var fixture = EasyDiskCatalogFixture.Create(workDirectory);
        var document = await ReadDirectAsync(fixture.DatabasePath);
        var byId = IndexById(document);

        Assert.Equal(1 + 2 + 3, document.Rows.Count);

        var disk = byId["disk:1"];
        Assert.Equal("Backup 2024", TextOf(disk, "name"));
        Assert.Equal("media", TextOf(disk, "type"));
        Assert.Null(disk["parentId"]);
        var diskProperties = PropertiesOf(disk);
        Assert.Equal("Unknown", Convert.ToString(diskProperties["DiskType"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Software / Games", Convert.ToString(diskProperties["Category"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Home / Shelf A", Convert.ToString(diskProperties["Location"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("2024-03-10T14:22:31", Convert.ToString(diskProperties["ScanDate"], System.Globalization.CultureInfo.InvariantCulture));

        var documents = byId["folder:1"];
        Assert.Equal("Documents", TextOf(documents, "name"));
        Assert.Equal("folder", TextOf(documents, "type"));
        Assert.Equal("disk:1", TextOf(documents, "parentId"));

        var projects = byId["folder:2"];
        Assert.Equal("Projects", TextOf(projects, "name"));
        Assert.Equal("folder:1", TextOf(projects, "parentId"));

        var readme = byId["file:1"];
        Assert.Equal("readme.txt", TextOf(readme, "name"));
        Assert.Equal("folder:1", TextOf(readme, "parentId"));
        Assert.Equal(512L, readme["size"]);
        var readmeProperties = PropertiesOf(readme);
        Assert.Equal("305441741", Convert.ToString(readmeProperties["Crc32"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Author: Admin", Convert.ToString(readmeProperties["ExtractedMetadata"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("keep-me", Convert.ToString(readmeProperties["Extension.CustomFlag"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("ReadOnly", Convert.ToString(readmeProperties["Attributes"], System.Globalization.CultureInfo.InvariantCulture));

        var loose = byId["file:3"];
        Assert.Equal("disk:1", TextOf(loose, "parentId"));
        Assert.Equal("4294967295", Convert.ToString(PropertiesOf(loose)["Crc32"], System.Globalization.CultureInfo.InvariantCulture));

        Assert.DoesNotContain(document.Rows, row => string.IsNullOrWhiteSpace(TextOf(row, "name")));
        Assert.True(document.Summary.SkippedRows >= 1);

        Assert.True(document.Summary.OpenedEncrypted);
        Assert.Equal(1, document.Summary.Disks);
        Assert.Equal(2, document.Summary.Folders);
        Assert.Equal(3, document.Summary.Files);
        Assert.Equal(2, document.Summary.Categories);
        Assert.Equal(2, document.Summary.Locations);
        Assert.Equal(1, document.Summary.Contacts);
        Assert.Equal(1, document.Summary.CurrentLoans);
        Assert.Equal(1, document.Summary.LoanHistoryEntries);
        Assert.Equal(1, document.Summary.UnknownTables);
    }

    [Fact]
    public async Task LoanAndContactData_AreRetainedAsExtensionMetadata()
    {
        var fixture = EasyDiskCatalogFixture.Create(workDirectory);
        var document = await ReadDirectAsync(fixture.DatabasePath);
        var diskProperties = PropertiesOf(IndexById(document)["disk:1"]);

        Assert.Equal("Jane Doe", Convert.ToString(diskProperties["Loan.Contact"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("2024-04-01", Convert.ToString(diskProperties["Loan.StartDate"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("current", Convert.ToString(diskProperties["Loan.Status"], System.Globalization.CultureInfo.InvariantCulture));
        var history = Convert.ToString(diskProperties["LoanHistory"], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        Assert.Contains("Jane Doe", history, StringComparison.Ordinal);
        Assert.Contains("returned", history, StringComparison.Ordinal);
        var directory = Convert.ToString(diskProperties["ContactDirectory"], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        Assert.Contains("jane@example.com", directory, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Previews_ResolveWhenPresentAndDegradeWhenMissing()
    {
        var fixture = EasyDiskCatalogFixture.Create(workDirectory);
        var document = await ReadDirectAsync(fixture.DatabasePath);
        var byId = IndexById(document);

        var readmeProperties = PropertiesOf(byId["file:1"]);
        Assert.Equal("found", Convert.ToString(readmeProperties["PreviewStatus"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(fixture.PresentPreview, Convert.ToString(readmeProperties["PreviewFile"], System.Globalization.CultureInfo.InvariantCulture));

        var appProperties = PropertiesOf(byId["file:2"]);
        Assert.Equal("missing", Convert.ToString(appProperties["PreviewStatus"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.False(appProperties.ContainsKey("PreviewFile"));

        var looseProperties = PropertiesOf(byId["file:3"]);
        Assert.False(looseProperties.ContainsKey("PreviewStatus"));

        Assert.Equal(1, document.Summary.PreviewsFound);
        Assert.Equal(1, document.Summary.PreviewsMissing);
    }

    [Fact]
    public async Task StreamImport_WithoutSourcePath_LeavesPreviewsUnresolved()
    {
        var fixture = EasyDiskCatalogFixture.Create(workDirectory);
        var plugin = new EasyDiskCatalogPlugin();
        using var stream = new MemoryStream(await File.ReadAllBytesAsync(fixture.DatabasePath));
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "edcdb",
            Source = stream,
            FileName = "fixture Database.edcdb",
            SourcePath = null
        });

        Assert.True(result.Success, $"Import failed: {result.Error}");
        var document = Assert.IsType<EasyDiskCatalogReportDocument>(result.Payload);
        var byId = IndexById(document);
        Assert.Equal("unresolved", Convert.ToString(PropertiesOf(byId["file:1"])["PreviewStatus"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(document.Summary.PreviewsUnresolved >= 1);
    }

    [Fact]
    public async Task SourceFile_IsUnchangedAfterImport()
    {
        var fixture = EasyDiskCatalogFixture.Create(workDirectory);
        using var sha = SHA256.Create();
        var before = Convert.ToHexString(sha.ComputeHash(await File.ReadAllBytesAsync(fixture.DatabasePath)));
        var previewBefore = Convert.ToHexString(sha.ComputeHash(await File.ReadAllBytesAsync(fixture.PresentPreview)));

        await ReadDirectAsync(fixture.DatabasePath);

        var after = Convert.ToHexString(sha.ComputeHash(await File.ReadAllBytesAsync(fixture.DatabasePath)));
        var previewAfter = Convert.ToHexString(sha.ComputeHash(await File.ReadAllBytesAsync(fixture.PresentPreview)));
        Assert.Equal(before, after);
        Assert.Equal(previewBefore, previewAfter);
    }

    [Fact]
    public async Task WrongPassword_ReturnsDecryptionDiagnostic()
    {
        var databasePath = EasyDiskCatalogFixture.CreateOtherPassword(workDirectory);
        var plugin = new EasyDiskCatalogPlugin();
        await using var stream = File.OpenRead(databasePath);
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "edcdb",
            Source = stream,
            FileName = Path.GetFileName(databasePath),
            SourcePath = databasePath
        });

        Assert.False(result.Success);
        Assert.Contains("cannot be decoded", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorruptTinyFile_ReturnsCorruptDiagnostic()
    {
        var databasePath = Path.Combine(workDirectory, "tiny.edcdb");
        Directory.CreateDirectory(workDirectory);
        await File.WriteAllBytesAsync(databasePath, [0x01, 0x02, 0x03, 0x04, 0x05]);

        var plugin = new EasyDiskCatalogPlugin();
        await using var stream = File.OpenRead(databasePath);
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "edcdb",
            Source = stream,
            FileName = "tiny.edcdb",
            SourcePath = databasePath
        });

        Assert.False(result.Success);
        Assert.Contains("corrupt or truncated", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlaintextNonCatalog_ReturnsUnsupportedDiagnostic()
    {
        var databasePath = EasyDiskCatalogFixture.CreatePlaintext(workDirectory, withCatalogSchema: false);
        var plugin = new EasyDiskCatalogPlugin();
        await using var stream = File.OpenRead(databasePath);
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "edcdb",
            Source = stream,
            FileName = Path.GetFileName(databasePath),
            SourcePath = databasePath
        });

        Assert.False(result.Success);
        Assert.Contains("not an Easy Disk Catalog", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlaintextCatalogSchema_ImportsWithoutPassword()
    {
        var databasePath = EasyDiskCatalogFixture.CreatePlaintext(workDirectory, withCatalogSchema: true);
        var plugin = new EasyDiskCatalogPlugin();
        await using var stream = File.OpenRead(databasePath);
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "edcdb",
            Source = stream,
            FileName = Path.GetFileName(databasePath),
            SourcePath = databasePath
        });

        Assert.True(result.Success, $"Import failed: {result.Error}");
        var document = Assert.IsType<EasyDiskCatalogReportDocument>(result.Payload);
        Assert.False(document.Summary.OpenedEncrypted);
        Assert.Single(document.Rows);
    }

    [Fact]
    public void Detection_AcceptsCatalogsAndRejectsOtherContent()
    {
        var fixture = EasyDiskCatalogFixture.Create(workDirectory);
        var plugin = new EasyDiskCatalogPlugin();

        using (var encrypted = File.OpenRead(fixture.DatabasePath))
        {
            var position = encrypted.Position;
            Assert.True(plugin.CanHandleContent(encrypted, "fixture Database.edcdb"));
            Assert.Equal(position, encrypted.Position);
        }

        var plainCatalog = EasyDiskCatalogFixture.CreatePlaintext(workDirectory, withCatalogSchema: true);
        using (var plain = File.OpenRead(plainCatalog))
        {
            Assert.True(plugin.CanHandleContent(plain, "plain-catalog.db"));
        }

        var plainOther = EasyDiskCatalogFixture.CreatePlaintext(workDirectory, withCatalogSchema: false);
        using (var other = File.OpenRead(plainOther))
        {
            Assert.False(plugin.CanHandleContent(other, "plain-other.db"));
        }

        using (var garbage = new MemoryStream([0x01, 0x02, 0x03, 0x04]))
        {
            Assert.False(plugin.CanHandleContent(garbage, "tiny.edcdb"));
        }

        using (var nonSeekable = new NonSeekableStream(new MemoryStream([0x01, 0x02])))
        {
            Assert.False(plugin.CanHandleContent(nonSeekable, "x.edcdb"));
        }
    }

    [Fact]
    public async Task CancelledImport_ThrowsOperationCanceled()
    {
        var fixture = EasyDiskCatalogFixture.Create(workDirectory);
        var plugin = new EasyDiskCatalogPlugin();
        await using var stream = File.OpenRead(fixture.DatabasePath);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => plugin.ReadAsync(
            new FileFormatReadRequest
            {
                FormatId = "edcdb",
                Source = stream,
                FileName = Path.GetFileName(fixture.DatabasePath),
                SourcePath = fixture.DatabasePath
            },
            cancelled.Token));
    }

    [Fact]
    public void Plugin_DoesNotDependOnOriginalApplicationStack()
    {
        var referenced = typeof(EasyDiskCatalogPlugin).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();
        Assert.DoesNotContain(referenced, name => name.Contains("SQLite", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("PCLRaw", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("MC", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, name => name.Equals("System.Data.SQLite", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
