using System;
using System.IO;

namespace SkyCD.Plugin.Metadata.DiskImages.Tests;

/// <summary>Writes image files into a throw-away directory so path-based opening can be exercised.</summary>
internal sealed class TempQcow2Directory : IDisposable
{
    private TempQcow2Directory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TempQcow2Directory Create()
    {
        return new TempQcow2Directory(Directory.CreateTempSubdirectory("skycd-qcow2-test-").FullName);
    }

    public string Write(string fileName, byte[] content)
    {
        var target = System.IO.Path.Combine(Path, fileName);
        File.WriteAllBytes(target, content);
        return target;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort test cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort test cleanup.
        }
    }
}
