using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SkyCD.Plugin.Storage;

public sealed class TemporaryDatabaseTests
{
    [Fact]
    public void CreateNew_UsesUniqueGuidNamesWithoutCreatingTheFile()
    {
        using var first = TemporaryDatabase.CreateNew(".testdb-unique");
        using var second = TemporaryDatabase.CreateNew(".testdb-unique");

        Assert.NotEqual(first.Path, second.Path);
        Assert.False(File.Exists(first.Path));
        Assert.False(File.Exists(second.Path));
        Assert.Equal(
            Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            Path.GetDirectoryName(first.Path)!.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void CreateNew_HonoursCustomDirectoryAndExtension()
    {
        var directory = CreateDirectory();
        try
        {
            using var database = TemporaryDatabase.CreateNew("testdb", directory);

            Assert.Equal(directory, Path.GetDirectoryName(database.Path));
            Assert.EndsWith(".testdb", database.Path, StringComparison.Ordinal);
            Assert.False(File.Exists(database.Path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CopyFrom_CopiesSourceBytes()
    {
        var content = new byte[4096];
        Random.Shared.NextBytes(content);
        using var source = new MemoryStream(content);

        using var database = TemporaryDatabase.CopyFrom(source, ".testdb-copy");

        Assert.Equal(content, File.ReadAllBytes(database.Path));
    }

    [Fact]
    public async Task CopyFromAsync_CopiesSourceBytes()
    {
        var content = "SQLite format 3\0payload"u8.ToArray();
        using var source = new MemoryStream(content);

        await using var database = await TemporaryDatabase.CopyFromAsync(source, ".testdb-async");

        Assert.Equal(content, File.ReadAllBytes(database.Path));
    }

    [Fact]
    public void CopyFrom_CopiesWalAndShmSidecarsFromKnownSource()
    {
        var directory = CreateDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "catalog.db");
            File.WriteAllBytes(sourcePath, [1, 2, 3]);
            File.WriteAllBytes(sourcePath + "-wal", [4, 5]);
            File.WriteAllBytes(sourcePath + "-shm", [6, 7]);
            File.WriteAllBytes(sourcePath + "-journal", [8]);

            using var source = File.OpenRead(sourcePath);
            using var database = TemporaryDatabase.CopyFrom(source, ".db", sidecarSourcePath: sourcePath);

            Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(database.Path + "-wal"));
            Assert.Equal(new byte[] { 6, 7 }, File.ReadAllBytes(database.Path + "-shm"));
            Assert.False(File.Exists(database.Path + "-journal"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CopyFrom_ExceedingSeekableLimit_ThrowsCallerExceptionAndLeavesNoFile()
    {
        using var source = new MemoryStream(new byte[64]);
        var limit = new InvalidOperationException("limit reached");

        var thrown = Assert.Throws<InvalidOperationException>(() =>
            TemporaryDatabase.CopyFrom(source, ".testdb-limit-seek", maxBytes: 32, limitExceeded: _ => limit));

        Assert.Same(limit, thrown);
        AssertNoTemporaryFiles(".testdb-limit-seek");
    }

    [Fact]
    public void CopyFrom_ExceedingStreamingLimit_ThrowsCallerExceptionAndLeavesNoFile()
    {
        using var source = new NonSeekableStream(new byte[128]);
        var limit = new InvalidOperationException("limit reached");

        var thrown = Assert.Throws<InvalidOperationException>(() =>
            TemporaryDatabase.CopyFrom(source, ".testdb-limit-stream", maxBytes: 32, limitExceeded: _ => limit));

        Assert.Same(limit, thrown);
        AssertNoTemporaryFiles(".testdb-limit-stream");
    }

    [Fact]
    public void CopyFrom_WhenCancelled_LeavesNoFile()
    {
        using var source = new MemoryStream(new byte[256]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            TemporaryDatabase.CopyFrom(source, ".testdb-cancel", cancellationToken: cancellation.Token));

        AssertNoTemporaryFiles(".testdb-cancel");
    }

    [Fact]
    public void Dispose_RemovesDatabaseAndAllSidecars()
    {
        var database = TemporaryDatabase.CreateNew(".testdb-dispose");
        File.WriteAllBytes(database.Path, [1]);
        File.WriteAllBytes(database.Path + "-wal", [1]);
        File.WriteAllBytes(database.Path + "-shm", [1]);
        File.WriteAllBytes(database.Path + "-journal", [1]);

        database.Dispose();

        Assert.False(File.Exists(database.Path));
        Assert.False(File.Exists(database.Path + "-wal"));
        Assert.False(File.Exists(database.Path + "-shm"));
        Assert.False(File.Exists(database.Path + "-journal"));
    }

    [Fact]
    public async Task DisposeAsync_RemovesDatabase()
    {
        var database = await TemporaryDatabase.CopyFromAsync(
            new MemoryStream([1, 2, 3]), ".testdb-dispose-async");

        await database.DisposeAsync();

        Assert.False(File.Exists(database.Path));
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-tempdb-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void AssertNoTemporaryFiles(string extension)
    {
        Assert.Empty(Directory.EnumerateFiles(Path.GetTempPath(), $"skycd-database-*{extension}"));
    }

    private sealed class NonSeekableStream(byte[] content) : Stream
    {
        private readonly MemoryStream inner = new(content);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }
    }
}
