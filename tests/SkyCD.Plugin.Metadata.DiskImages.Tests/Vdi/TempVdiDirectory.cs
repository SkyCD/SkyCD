using System;
using System.IO;

namespace SkyCD.Plugin.Metadata.DiskImages.Tests;

/// <summary>Writes image files into a throw-away directory so path-based opening can be exercised.</summary>
internal sealed class TempVdiDirectory : IDisposable
{
    private TempVdiDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TempVdiDirectory Create()
    {
        return new TempVdiDirectory(Directory.CreateTempSubdirectory("skycd-vdi-test-").FullName);
    }

    public string Resolve(string fileName) => System.IO.Path.Combine(Path, fileName);

    public string Write(string fileName, byte[] content)
    {
        var target = Resolve(fileName);
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
