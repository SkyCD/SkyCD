using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using Xunit;

namespace SkyCD.Plugin.Katalog.Sqlite.Tests;

public sealed class KatalogSqliteDetectionTests : IDisposable
{
    private readonly KatalogFixtureBuilder fixtures = new();
    private readonly KatalogSqlitePlugin plugin = new();

    public void Dispose() => fixtures.Dispose();

    [Fact]
    public void SupportedFormat_HasExpectedMetadata()
    {
        var format = plugin.SupportedFormat;

        Assert.Equal("katalog-sqlite", format.FormatId);
        Assert.Equal("Katalog SQLite Collection (*.db)", format.DisplayName);
        Assert.Contains(".db", format.Extensions);
        Assert.Contains(".sqlite", format.Extensions);
        Assert.True(format.CanRead);
        Assert.False(format.CanWrite);
    }

    [Fact]
    public void CanHandleContent_ReturnsTrueForKatalogDatabase()
    {
        var path = fixtures.CreateFullCollection("detect.db");
        using var stream = File.OpenRead(path);

        Assert.True(plugin.CanHandleContent(stream, "detect.db"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForUnrelatedSqliteDatabase()
    {
        var path = fixtures.CreateNonKatalogDatabase("plain.db");
        using var stream = File.OpenRead(path);

        Assert.False(plugin.CanHandleContent(stream, "plain.db"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForNonDatabase()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("definitely not a database file"));

        Assert.False(plugin.CanHandleContent(stream, "notes.db"));
    }

    [Fact]
    public async Task WriteAsync_IsNotSupported()
    {
        await using var target = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = target,
            FormatId = "katalog-sqlite",
            Payload = new object()
        });

        Assert.False(result.Success);
    }
}
