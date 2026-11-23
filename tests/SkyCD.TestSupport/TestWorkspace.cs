using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.TestSupport;

/// <summary>
/// Owns a unique throwaway directory under the system temp path so tests never write into the
/// repository tree. Create it once per test (field, local <c>using var</c>, or fixture), then rely on
/// <see cref="Dispose"/> or <see cref="DisposeAsync"/> to delete everything it created.
/// </summary>
/// <remarks>
/// The root (<c>skycd-tests/&lt;guid&gt;</c>) is created on first use, not in the constructor.
/// Disposal never throws: a tree that cannot be deleted is simply left behind, so a workspace that is
/// never disposed (or whose deletion keeps failing) leaves exactly one <c>skycd-tests/&lt;guid&gt;</c>
/// directory in the temp path for manual cleanup.
/// </remarks>
public sealed class TestWorkspace : IDisposable, IAsyncDisposable
{
    private const string RootFolderName = "skycd-tests";
    private const int DeleteAttempts = 5;
    private const int DeleteRetryDelayMilliseconds = 50;

    private readonly string root;
    private bool disposed;

    /// <summary>Creates a workspace whose root is not created until the first path is requested.</summary>
    public TestWorkspace()
    {
        root = Path.Combine(Path.GetTempPath(), RootFolderName, Guid.NewGuid().ToString("N"));
    }

    /// <summary>Absolute path of the workspace root; the directory is created on first access.</summary>
    public string Root
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Directory.CreateDirectory(root);
            return root;
        }
    }

    /// <summary>Absolute path for <paramref name="relativePath"/> inside the workspace, without creating it.</summary>
    public string GetPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ObjectDisposedException.ThrowIf(disposed, this);
        return Path.Combine(Root, relativePath);
    }

    /// <summary>Creates parent directories and writes <paramref name="bytes"/>; returns the absolute path.</summary>
    public string CreateFile(string relativePath, ReadOnlySpan<byte> bytes)
    {
        var path = GetPath(relativePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        return path;
    }

    /// <summary>Creates parent directories and writes UTF-8 text; returns the absolute path.</summary>
    public string CreateFile(string relativePath, string contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        return CreateFile(relativePath, System.Text.Encoding.UTF8.GetBytes(contents));
    }

    /// <summary>Creates an empty file (and its parents); returns the absolute path.</summary>
    public string CreateEmptyFile(string relativePath) => CreateFile(relativePath, ReadOnlySpan<byte>.Empty);

    /// <summary>Creates a directory (and its parents); returns the absolute path.</summary>
    public string CreateDirectory(string relativePath)
    {
        var path = GetPath(relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Deletes the workspace tree; never throws and is safe to call more than once.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DeleteTree(root);
    }

    /// <summary>Deletes the workspace tree; the deletion is synchronous, so this simply calls <see cref="Dispose"/>.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static void DeleteTree(string path)
    {
        for (var attempt = 1; attempt <= DeleteAttempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                ClearReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == DeleteAttempts)
                {
                    return;
                }
            }

            Thread.Sleep(DeleteRetryDelayMilliseconds * attempt);
        }
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        foreach (var file in EnumerateFilesSafely(path))
        {
            TryClearReadOnly(file);
        }

        foreach (var directory in EnumerateDirectoriesSafely(path))
        {
            ClearReadOnlyAttributes(directory);
            TryClearReadOnly(directory);
        }
    }

    private static IEnumerable<string> EnumerateFilesSafely(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafely(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void TryClearReadOnly(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that vanishes or stays locked is handled by the retrying caller.
        }
    }
}
