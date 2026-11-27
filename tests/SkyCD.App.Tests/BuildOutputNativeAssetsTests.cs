using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1319: a test output must carry only the build host's native runtime assets, and no native
/// debug symbols, so a RID-less test build does not materialise every platform's binaries.
/// </summary>
public sealed class BuildOutputNativeAssetsTests
{
    private static readonly string RuntimesDirectory = Path.Combine(AppContext.BaseDirectory, "runtimes");

    [Fact]
    public void RuntimesDirectoryContainsOnlyHostPlatformAssets()
    {
        if (!Directory.Exists(RuntimesDirectory))
        {
            return;
        }

        var foreign = Directory.EnumerateDirectories(RuntimesDirectory)
            .Select(directory => Path.GetFileName(directory))
            .Where(rid => !IsHostPlatformOrShared(rid))
            .ToArray();

        Assert.Empty(foreign);
    }

    [Fact]
    public void RuntimesDirectoryContainsNoNativeDebugSymbols()
    {
        if (!Directory.Exists(RuntimesDirectory))
        {
            return;
        }

        Assert.Empty(Directory.EnumerateFiles(RuntimesDirectory, "*.pdb", SearchOption.AllDirectories));
    }

    private static bool IsHostPlatformOrShared(string rid)
    {
        if (rid.StartsWith("any", StringComparison.OrdinalIgnoreCase) ||
            rid.StartsWith("unix", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return OperatingSystem.IsWindows()
            ? rid.StartsWith("win", StringComparison.OrdinalIgnoreCase)
            : OperatingSystem.IsMacOS()
                ? rid.StartsWith("osx", StringComparison.OrdinalIgnoreCase)
                : rid.StartsWith("linux", StringComparison.OrdinalIgnoreCase);
    }
}