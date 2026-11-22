using System;
using System.IO;

namespace SkyCD.Plugin.FileFormat.Vhd.Tests;

/// <summary>Writes image files into a throw-away directory so path-based opening can be exercised.</summary>
internal sealed class TempVhdDirectory : IDisposable
{
    private TempVhdDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TempVhdDirectory Create()
    {
        return new TempVhdDirectory(Directory.CreateTempSubdirectory("skycd-vhd-test-").FullName);
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
