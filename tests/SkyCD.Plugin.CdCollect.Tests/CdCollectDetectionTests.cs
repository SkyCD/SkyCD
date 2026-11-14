using System;
using System.IO;
using SkyCD.Plugin.CdCollect;
using Xunit;

namespace SkyCD.Plugin.CdCollect.Tests;

public sealed class CdCollectDetectionTests
{
    [Fact]
    public void CanHandleContent_ValidCdCollectDatabase_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cdcollect_det_" + Guid.NewGuid().ToString("N"));
        try
        {
            CdCollectTestHelper.EnsureFixtureFilesExist(tempDir);
            var dbPath = Path.Combine(tempDir, "cdcollect-sample-v4.db");

            var plugin = new CdCollectPlugin();
            using var fileStream = File.OpenRead(dbPath);

            var canHandle = plugin.CanHandleContent(fileStream, "mycatalog.db");
            Assert.True(canHandle);
            Assert.Equal(0, fileStream.Position);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void CanHandleContent_CdCollectExtension_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cdcollect_det_" + Guid.NewGuid().ToString("N"));
        try
        {
            CdCollectTestHelper.EnsureFixtureFilesExist(tempDir);
            var dbPath = Path.Combine(tempDir, "cdcollect-sample-v4.db");

            var plugin = new CdCollectPlugin();
            using var fileStream = File.OpenRead(dbPath);

            var canHandle = plugin.CanHandleContent(fileStream, "backup.cdcollect");
            Assert.True(canHandle);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void CanHandleContent_NonCdCollectDatabase_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cdcollect_det_" + Guid.NewGuid().ToString("N"));
        try
        {
            CdCollectTestHelper.EnsureFixtureFilesExist(tempDir);
            var dbPath = Path.Combine(tempDir, "non-cdcollect.db");

            var plugin = new CdCollectPlugin();
            using var fileStream = File.OpenRead(dbPath);

            var canHandle = plugin.CanHandleContent(fileStream, "non-cdcollect.db");
            Assert.False(canHandle);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
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
