using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Storage;

/// <summary>
/// Owns a uniquely named database file in the temporary directory (or a caller-chosen directory)
/// and removes it together with any SQLite <c>-wal</c>, <c>-shm</c> and <c>-journal</c> sidecars when
/// disposed. Names are derived from a GUID instead of <see cref="Path.GetTempFileName"/>, so a killed
/// process never leaves an empty placeholder behind.
/// </summary>
internal sealed class TemporaryDatabase : IDisposable, IAsyncDisposable
{
    private const int CopyBufferSize = 81920;
    private static readonly string[] SidecarSuffixes = ["-wal", "-shm", "-journal"];
    private static readonly string[] CopiedSidecarSuffixes = ["-wal", "-shm"];

    private TemporaryDatabase(string path)
    {
        Path = path;
    }

    /// <summary>Full path reserved for this database. The file only exists once a caller creates it.</summary>
    public string Path { get; }

    /// <summary>
    /// Reserves a unique, unused path. The caller is responsible for creating the file, for example
    /// with a database library that needs a path rather than a stream.
    /// </summary>
    public static TemporaryDatabase CreateNew(string extension, string? directory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        var root = string.IsNullOrWhiteSpace(directory) ? System.IO.Path.GetTempPath() : directory;
        var suffix = extension.StartsWith('.') ? extension : "." + extension;
        return new TemporaryDatabase(System.IO.Path.Combine(root, $"skycd-database-{Guid.NewGuid():N}{suffix}"));
    }

    /// <summary>
    /// Snapshots <paramref name="source"/> into a new temporary file. When the stream length is known
    /// it is checked before copying and, in every case, the copied byte count is bounded while the data
    /// streams; exceeding <paramref name="maxBytes"/> throws the exception produced by
    /// <paramref name="limitExceeded"/>. A known <paramref name="sidecarSourcePath"/> contributes
    /// <c>-wal</c>/<c>-shm</c> files on a best-effort basis. Any partial file is removed when the copy fails.
    /// </summary>
    public static TemporaryDatabase CopyFrom(
        Stream source,
        string extension,
        long maxBytes = long.MaxValue,
        Func<long, Exception>? limitExceeded = null,
        string? sidecarSourcePath = null,
        CancellationToken cancellationToken = default) =>
        CopyFromAsync(source, extension, maxBytes, limitExceeded, sidecarSourcePath, cancellationToken)
            .GetAwaiter()
            .GetResult();

    /// <inheritdoc cref="CopyFrom"/>
    public static async Task<TemporaryDatabase> CopyFromAsync(
        Stream source,
        string extension,
        long maxBytes = long.MaxValue,
        Func<long, Exception>? limitExceeded = null,
        string? sidecarSourcePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var database = CreateNew(extension);
        try
        {
            if (source.CanSeek && source.Length - source.Position > maxBytes)
            {
                throw LimitException(maxBytes, limitExceeded);
            }

            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using (var target = new FileStream(database.Path, options))
            {
                var buffer = new byte[CopyBufferSize];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        throw LimitException(maxBytes, limitExceeded);
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            database.CopySidecars(sidecarSourcePath);
            return database;
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var suffix in SidecarSuffixes)
        {
            DeleteQuietly(Path + suffix);
        }

        DeleteQuietly(Path);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void CopySidecars(string? sidecarSourcePath)
    {
        if (string.IsNullOrWhiteSpace(sidecarSourcePath))
        {
            return;
        }

        foreach (var suffix in CopiedSidecarSuffixes)
        {
            try
            {
                var sidecar = sidecarSourcePath + suffix;
                if (File.Exists(sidecar))
                {
                    File.Copy(sidecar, Path + suffix, overwrite: true);
                }
            }
            catch (IOException)
            {
                // Best effort: the main file alone still yields a consistent older snapshot.
            }
        }
    }

    private static Exception LimitException(long maxBytes, Func<long, Exception>? limitExceeded) =>
        limitExceeded?.Invoke(maxBytes)
        ?? new IOException($"The temporary database exceeds the {maxBytes} byte limit.");

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
