using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SkyCD.TestSupport.Tests;

/// <summary>
/// Issue #1225: the shared helpers had been copied into every plugin test project and drifted apart, so a
/// copy that reappears now fails the suite instead of growing back unnoticed.
/// </summary>
public sealed class SharedHelperDuplicationGateTests
{
    [Theory]
    [InlineData("ByteArrayMediaSource.cs", "tests/SkyCD.TestSupport/ByteArrayMediaSource.cs")]
    [InlineData("TestHelpers.cs", null)]
    public void HelperFile_ExistsOnlyAtTheCanonicalLocation(string fileName, string? canonicalRelativePath)
    {
        var repositoryRoot = FindRepositoryRoot();

        var copies = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "tests"), fileName, SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] expected = canonicalRelativePath is null ? [] : [canonicalRelativePath];
        Assert.Equal(expected, copies);
    }

    private const string DeclarationMarker = "class " + nameof(ByteArrayMediaSource);

    [Fact]
    public void ByteArrayMediaSource_IsDeclaredOnlyInTestSupport()
    {
        var repositoryRoot = FindRepositoryRoot();

        var declaringFiles = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains(DeclarationMarker, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["tests/SkyCD.TestSupport/ByteArrayMediaSource.cs"], declaringFiles);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SkyCD.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Could not find SkyCD.slnx above '{AppContext.BaseDirectory}'.");
    }
}
