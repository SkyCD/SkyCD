using System;
using System.IO;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Storage;

/// <summary>
/// Owns the extraction root of a single index run. The directory is created on construction under the
/// system temporary directory with a format-specific, unique name, and is removed on disposal. Cleanup is
/// best effort and never throws, so a failed or cancelled index run still removes what it wrote.
/// </summary>
internal sealed class ArchiveExtractionWorkspace : IDisposable, IAsyncDisposable
{
    private bool disposed;

    public ArchiveExtractionWorkspace(string formatName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatName);
        Root = Path.Combine(Path.GetTempPath(), $"skycd-{formatName}-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
    }

    /// <summary>Full path of the extraction directory this workspace owns.</summary>
    public string Root { get; }

    /// <summary>Gets a path inside <see cref="Root"/> for the given simple file name.</summary>
    public string GetPath(string fileName) => Path.Combine(Root, fileName);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DeleteQuietly(Root);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static void DeleteQuietly(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                DeleteFileQuietly(file);
            }

            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup: leave the rest for OS temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup: leave the rest for OS temp cleanup.
        }
    }

    private static void DeleteFileQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }
}
