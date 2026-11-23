using System.IO;
using SkyCD.Plugin.FileFormat.CDCollect;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.FileFormat.CDCollect.Tests;

public sealed class CdCollectDetectionTests
{
    [Fact]
    public void CanHandleContent_ValidCdCollectDatabase_ReturnsTrue()
    {
        using var workspace = new TestWorkspace();
        var dbPath = CdCollectTestHelper.EnsureSampleV4DatabaseExists(workspace);

        var plugin = new CdCollectPlugin();
        using var fileStream = File.OpenRead(dbPath);

        var canHandle = plugin.CanHandleContent(fileStream, "mycatalog.db");
        Assert.True(canHandle);
        Assert.Equal(0, fileStream.Position);
    }

    [Fact]
    public void CanHandleContent_CdCollectExtension_ReturnsTrue()
    {
        using var workspace = new TestWorkspace();
        var dbPath = CdCollectTestHelper.EnsureSampleV4DatabaseExists(workspace);

        var plugin = new CdCollectPlugin();
        using var fileStream = File.OpenRead(dbPath);

        var canHandle = plugin.CanHandleContent(fileStream, "backup.cdcollect");
        Assert.True(canHandle);
    }

    [Fact]
    public void CanHandleContent_NonCdCollectDatabase_ReturnsFalse()
    {
        using var workspace = new TestWorkspace();
        var dbPath = CdCollectTestHelper.EnsureNonCdCollectDatabaseExists(workspace);

        var plugin = new CdCollectPlugin();
        using var fileStream = File.OpenRead(dbPath);

        var canHandle = plugin.CanHandleContent(fileStream, "non-cdcollect.db");
        Assert.False(canHandle);
    }

    [Fact]
    public void CanHandleContent_InvalidBytes_ReturnsFalse()
    {
        var plugin = new CdCollectPlugin();
        using var stream = new MemoryStream([1, 2, 3, 4, 5]);

        var canHandle = plugin.CanHandleContent(stream, "invalid.db");
        Assert.False(canHandle);
    }

    [Fact]
    public void CanHandleContent_EmptyStream_ReturnsFalse()
    {
        var plugin = new CdCollectPlugin();
        using var stream = new MemoryStream();

        var canHandle = plugin.CanHandleContent(stream, "empty.db");
        Assert.False(canHandle);
    }
}
