using System;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.DiskImages.Tests;

/// <summary>Writes image files into a throw-away directory so path-based opening can be exercised.</summary>
internal sealed class TempDmgDirectory : IDisposable
{
    private readonly TestWorkspace workspace = new();

    public string Path => workspace.Root;

    public static TempDmgDirectory Create()
    {
        return new TempDmgDirectory();
    }

    public string Write(string fileName, byte[] content) => workspace.CreateFile(fileName, content);

    public void Dispose() => workspace.Dispose();
}
