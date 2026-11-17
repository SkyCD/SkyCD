using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Access;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

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
    public async Task UnavailableProvider_ReturnsActionableErrorWithoutWritingToTarget()
    {
        if (AccessListExportPlugin.FindAvailableProvider() is not null) return;
        await using var target = new MemoryStream();
        var result = await new AccessListExportPlugin().WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-access-list",
            Target = target,
            Payload = new AccessListExportRequest { Rows = [] }
        });
        Assert.False(result.Success);
        Assert.Equal(new AccessExportProviderUnavailableException().Message, result.Error);
        Assert.Equal(0, target.Length);
    }

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

    [AccessProviderFact]
    [SupportedOSPlatform("windows")]
    public async Task NativeDatabase_ReopensIndependentlyWithSelectedColumnsAndValuesInOrder()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"skycd-access-native-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        object? connectionObject = null;
        object? recordsetObject = null;
        try
        {
            var path = Path.Combine(directory, "list.mdb");
            var request = new AccessListExportRequest
            {
                Columns = [new("Name", "SELECT"), new("Other", "SELECT"), new("Empty", "Žąsis"), new("Size", "Size"), new("When", "When")],
                Rows = new IReadOnlyDictionary<string, object?>[]
                {
                    new Dictionary<string, object?> { ["Name"] = "O'Brien", ["Other"] = "Žąsis", ["Empty"] = "", ["Size"] = 1234L, ["When"] = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero) },
                    new Dictionary<string, object?> { ["Name"] = "Second", ["Other"] = null }
                }
            };
            var manager = new FileFormatManager([new AccessListExportPlugin()]);
            await manager.WriteFileAsync("skycd-access-list", path, request);
            Assert.Single(Directory.GetFiles(directory));
            var provider = AccessListExportPlugin.FindAvailableProvider();
            connectionObject = Activator.CreateInstance(Type.GetTypeFromProgID("ADODB.Connection")!);
            dynamic connection = connectionObject!;
            connection.Open($"Provider={provider};Data Source={path};");
            object schemaObject = connection.OpenSchema(20);
            dynamic schema = schemaObject;
            var tables = new List<string>();
            while (!(bool)schema.EOF)
            {
                if ((string)schema.Fields["TABLE_TYPE"].Value == "TABLE")
                    tables.Add((string)schema.Fields["TABLE_NAME"].Value);
                schema.MoveNext();
            }
            schema.Close();
            Marshal.FinalReleaseComObject(schemaObject);
            Assert.Equal(["ItemList"], tables);
            recordsetObject = connection.Execute("SELECT * FROM [ItemList]");
            dynamic rows = recordsetObject;
            Assert.Equal(5, (int)rows.Fields.Count);
            Assert.Equal("SELECT", (string)rows.Fields[0].Name);
            Assert.Equal("SELECT_2", (string)rows.Fields[1].Name);
            Assert.Equal("Žąsis", (string)rows.Fields[2].Name);
            Assert.Equal("Size", (string)rows.Fields[3].Name);
            Assert.Equal("When", (string)rows.Fields[4].Name);
            Assert.Equal("O'Brien", (string)rows.Fields[0].Value);
            Assert.Equal("Žąsis", (string)rows.Fields[1].Value);
            Assert.Equal("", (string)rows.Fields[2].Value);
            Assert.Equal("1234", (string)rows.Fields[3].Value);
            Assert.Equal("2026-01-02T03:04:05.0000000+00:00", (string)rows.Fields[4].Value);
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

    [AccessProviderFact]
    public async Task CancellationDuringNativeInserts_PreservesDestinationAndRemovesTemporaryFiles()
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

    [AccessProviderFact]
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

    private sealed class InlineProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}

public sealed class AccessProviderFactAttribute : FactAttribute
{
    public AccessProviderFactAttribute()
    {
        if (AccessListExportPlugin.FindAvailableProvider() is null)
            Skip = "Requires Windows and a matching Jet/ACE runtime provider; Microsoft Access is not required.";
    }
}
