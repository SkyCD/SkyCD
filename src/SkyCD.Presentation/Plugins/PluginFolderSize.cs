using System;
using System.IO;

namespace SkyCD.Presentation.Plugins;

/// <summary>Computes the on-disk size of the folder that holds a plugin assembly.</summary>
public static class PluginFolderSize
{
    public static long? TryComputeBytes(string? assemblyLocation)
    {
        if (string.IsNullOrWhiteSpace(assemblyLocation))
        {
            return null;
        }

        var folder = Path.GetDirectoryName(assemblyLocation);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        try
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return total;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
