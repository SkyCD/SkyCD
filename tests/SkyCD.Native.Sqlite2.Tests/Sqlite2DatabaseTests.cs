using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Text.Json;
using Xunit;

namespace SkyCD.Native.Sqlite2.Tests;

public sealed class Sqlite2DatabaseTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void BundledLibraries_MatchRecordedHashes()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sqlite2-sha256.json")));
        Assert.Equal(4, manifest.RootElement.EnumerateObject().Count());
        foreach (var entry in manifest.RootElement.EnumerateObject())
        {
            var path = Path.Combine(AppContext.BaseDirectory, entry.Name.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(entry.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        }
    }

    [Theory]
    [InlineData("cds.db")]
    [InlineData("big-endian.db")]
    public void BundledEngine_ReadsTablesAndPrimaryKeys_WithoutChangingSource(string name)
    {
        var before = SHA256.HashData(File.ReadAllBytes(Fixture(name)));
        using var source = File.OpenRead(Fixture(name));
        using var database = new Sqlite2Database(source);
        Assert.Equal("2.8.17", Sqlite2Database.EngineVersion);
        var media = database.ReadTable("cds");
        Assert.Equal(2, media.Count);
        Assert.Contains(media, row => row.RowId == 7 && row.Values[0] == "7" && row.Values[1] == "Legacy music");
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(Fixture(name))));
    }

    [Theory]
    [InlineData("CREATE TABLE must_not_exist(id INTEGER)")]
    [InlineData("DELETE FROM cds")]
    [InlineData("ATTACH DATABASE ':memory:' AS other")]
    [InlineData("PRAGMA writable_schema=ON")]
    public void Authorizer_DeniesWritesAndAttach(string sql)
    {
        using var source = File.OpenRead(Fixture("cds.db"));
        using var database = new Sqlite2Database(source);
        var query = typeof(Sqlite2Database).GetMethod("Query", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var invocation = Assert.Throws<TargetInvocationException>(() => query.Invoke(database, [sql]));
        Assert.IsType<Sqlite2Exception>(invocation.InnerException);
        Assert.Equal(2, database.ReadTable("cds").Count);
    }

    [Fact]
    public void ReadTable_RejectsSqlInIdentifiers()
    {
        using var source = File.OpenRead(Fixture("cds.db"));
        using var database = new Sqlite2Database(source);
        Assert.Throws<Sqlite2Exception>(() => database.ReadTable("cds; DELETE FROM cds"));
    }

    [Fact]
    public void ReadTable_RespectsCancellationAfterOpen()
    {
        using var cancellation = new CancellationTokenSource();
        using var source = File.OpenRead(Fixture("large.db"));
        using var database = new Sqlite2Database(source, cancellation.Token);
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => database.ReadTable("files"));
    }

    [Fact]
    public void Dispose_RejectsFurtherQueries()
    {
        using var source = File.OpenRead(Fixture("cds.db"));
        var database = new Sqlite2Database(source);
        database.Dispose();
        Assert.Throws<ObjectDisposedException>(() => database.ReadTable("cds"));
    }
}
