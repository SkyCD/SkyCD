using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.FileFormat.Access;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using UCanAccess.File;
using Xunit;
using SkyCD.Plugin.FileFormat.Access.Models;

namespace SkyCD.App.Tests;

public sealed class AccessListExportTests
{
    [Fact]
    public void Columns_NormalizeDeterministicallyWithoutLosingSupportedUnicode()
    {
        var columns = new[] { "SELECT", "select", "Žąsis", "", "bad.name", "SELECT_2", new string('x', 65) }
            .Select(name => new AccessListColumn(name, name)).ToArray();
        Assert.Equal(["SELECT", "select_2", "Žąsis", "Column4", "Column5", "SELECT_2_2", "Column7"],
            AccessListExportPlugin.NormalizeColumnNames(columns));
        Assert.False(new AccessListExportPlugin().SupportedFormat.CanRead);
        Assert.True(new AccessListExportPlugin().SupportedFormat.CanWrite);
    }

    [Fact]
    public void Values_UseInvariantNumbersAndIsoTimestamps()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("lt-LT");
            Assert.Equal("1234.5", AccessListExportPlugin.FormatValue(1234.5m));
            var timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
            Assert.Equal(timestamp.ToString("O", CultureInfo.InvariantCulture), AccessListExportPlugin.FormatValue(timestamp));
            Assert.Equal("", AccessListExportPlugin.FormatValue(null));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task AtomicHostExport_PreservesExistingDestinationWhenSerializationFails()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"skycd-access-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "existing.mdb");
            await File.WriteAllTextAsync(path, "original");
            var manager = new FileFormatManager([new FailingWriter()]);
            await Assert.ThrowsAsync<FileFormatWriteFailedException>(() => manager.WriteFileAsync("fail", path, new object()));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Database_ReopensWithSelectedColumnsRequiredValuesAndInsertionOrder()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"skycd-access-native-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "list.mdb");
            await new FileFormatManager([new AccessListExportPlugin()])
                .WriteFileAsync("skycd-access-list", path, SampleExport());

            var header = new byte[15];
            await using (var stream = File.OpenRead(path))
            {
                stream.Position = 4;
                await stream.ReadExactlyAsync(header);
            }
            Assert.Equal("Standard Jet DB", Encoding.ASCII.GetString(header));

            using (var database = Database.Open(path, readOnly: true))
            {
                Assert.Equal(["ItemList"], database.GetTableNames().ToArray());
                var table = database.GetTable("ItemList")!;
                Assert.Equal(["SELECT", "SELECT_2", "Žąsis", "Size", "When"],
                    table.Columns.Select(column => column.Name).ToArray());
                Assert.All(table.Columns, column =>
                {
                    Assert.True(column.Required);
                    Assert.Equal(510, column.ColumnLength);
                });

                var rows = table.Rows().Select(row => row.ToArray()).ToArray();
                Assert.Equal(2, rows.Length);
                Assert.Equal(new object[] { "O'Brien", "Žąsis", "", "1234", "2026-01-02T03:04:05.0000000+00:00" }, rows[0]);
                Assert.Equal(new object[] { "Second", "", "", "", "" }, rows[1]);
            }

            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CancellationDuringInserts_PreservesDestinationAndRemovesTemporaryFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"skycd-access-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "list.mdb");
            await File.WriteAllTextAsync(path, "original");
            using var cancellation = new CancellationTokenSource();
            var export = new AccessListExportRequest
            {
                Columns = [new("Name", "Name")],
                Rows = Enumerable.Range(0, 1000).Select(index => (IReadOnlyDictionary<string, object?>)
                    new Dictionary<string, object?> { ["Name"] = index.ToString(CultureInfo.InvariantCulture) })
            };
            var manager = new FileFormatManager([new AccessListExportPlugin()]);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.WriteFileAsync("skycd-access-list", path,
                export, new InlineProgress(_ => cancellation.Cancel()), cancellation.Token));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OversizedText_FailsWithoutTruncationOrReplacingDestination()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"skycd-access-long-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "list.mdb");
            await File.WriteAllTextAsync(path, "original");
            var export = new AccessListExportRequest
            {
                Columns = [new("Name", "Name")],
                Rows = [new Dictionary<string, object?> { ["Name"] = new string('x', 256) }]
            };
            var manager = new FileFormatManager([new AccessListExportPlugin()]);
            var error = await Assert.ThrowsAsync<FileFormatWriteFailedException>(() => manager.WriteFileAsync("skycd-access-list", path, export));
            Assert.Contains("255-character", error.Message);
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [AceProviderFact]
    [SupportedOSPlatform("windows")]
    public async Task AceEngine_ReopensGeneratedDatabaseWithLocaleSchemaAndValues()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"skycd-access-ace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        object? connectionObject = null;
        object? recordsetObject = null;
        try
        {
            var path = Path.Combine(directory, "list.mdb");
            await new FileFormatManager([new AccessListExportPlugin()])
                .WriteFileAsync("skycd-access-list", path, SampleExport());

            var provider = AceProviderFactAttribute.FindProvider();
            Assert.NotNull(provider);
            connectionObject = Activator.CreateInstance(Type.GetTypeFromProgID("ADODB.Connection")!);
            dynamic connection = connectionObject!;
            connection.Open($"Provider={provider};Data Source={path};");
            Assert.Equal(1033, (int)connection.Properties.Item("Locale Identifier").Value);

            recordsetObject = connection.Execute("SELECT * FROM [ItemList]");
            dynamic rows = recordsetObject!;
            Assert.Equal(5, (int)rows.Fields.Count);
            string[] expectedNames = ["SELECT", "SELECT_2", "Žąsis", "Size", "When"];
            for (var index = 0; index < expectedNames.Length; index++)
                Assert.Equal(expectedNames[index], (string)rows.Fields[index].Name);

            Assert.Equal(new object?[] { "O'Brien", "Žąsis", "", "1234", "2026-01-02T03:04:05.0000000+00:00" },
                Enumerable.Range(0, 5).Select(index => (object?)rows.Fields[index].Value).ToArray());
            rows.MoveNext();
            Assert.Equal("Second", (string)rows.Fields[0].Value);
            rows.MoveNext();
            Assert.True((bool)rows.EOF);
            rows.Close();
            connection.Close();
        }
        finally
        {
            if (recordsetObject is not null) Marshal.FinalReleaseComObject(recordsetObject);
            if (connectionObject is not null) Marshal.FinalReleaseComObject(connectionObject);
            Directory.Delete(directory, true);
        }
    }

    private static AccessListExportRequest SampleExport() => new()
    {
        Columns = [new("Name", "SELECT"), new("Other", "SELECT"), new("Empty", "Žąsis"), new("Size", "Size"), new("When", "When")],
        Rows = new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?>
            {
                ["Name"] = "O'Brien",
                ["Other"] = "Žąsis",
                ["Empty"] = "",
                ["Size"] = 1234L,
                ["When"] = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)
            },
            new Dictionary<string, object?> { ["Name"] = "Second", ["Other"] = null }
        }
    };

    private sealed class FailingWriter : IFileFormatPluginCapability
    {
        public FileFormatDescriptor SupportedFormat => new("fail", "Failing", [".mdb"], [], false, true);
        public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
        {
            await request.Target.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
            return new FileFormatWriteResult { Success = false, Error = "Provider unavailable" };
        }
    }

    private sealed class InlineProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}

public sealed class AceProviderFactAttribute : FactAttribute
{
    public AceProviderFactAttribute()
    {
        if (FindProvider() is null)
            Skip = "Requires Windows and an ACE or Jet OLE DB provider; Microsoft Access is not required.";
    }

    internal static string? FindProvider()
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var provider in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0", "Microsoft.Jet.OLEDB.4.0" })
            if (CanActivate(provider)) return provider;
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static bool CanActivate(string progId)
    {
        object? instance = null;
        try
        {
            var type = Type.GetTypeFromProgID(progId);
            if (type is null) return false;
            instance = Activator.CreateInstance(type);
            return instance is not null;
        }
        catch (COMException) { return false; }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
        }
    }
}
