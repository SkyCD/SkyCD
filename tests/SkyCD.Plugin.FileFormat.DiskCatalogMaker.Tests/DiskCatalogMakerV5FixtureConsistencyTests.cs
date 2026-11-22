using System;
using System.IO;
using Xunit;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Tests;

public sealed class DiskCatalogMakerV5FixtureConsistencyTests
{
    [Fact]
    public void CommittedFixtures_MatchGenerator()
    {
        var expectedDirectory = Path.Combine(Path.GetTempPath(), "skycd-dcm5-fixtures-" + Guid.NewGuid().ToString("N"));
        try
        {
            DiskCatalogMakerV5Fixtures.WriteAll(expectedDirectory);
            foreach (var expectedPath in Directory.EnumerateFiles(expectedDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(expectedDirectory, expectedPath);
                Assert.Equal(
                    File.ReadAllBytes(expectedPath),
                    File.ReadAllBytes(DiskCatalogMakerV5CheckedInFixtureTests.FixturePath(relative)));
            }
        }
        finally
        {
            if (Directory.Exists(expectedDirectory))
            {
                Directory.Delete(expectedDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void RegenerateCommittedFixtures_WhenRequested()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SKYCD_REGENERATE_DCM5_FIXTURES"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        DiskCatalogMakerV5Fixtures.WriteAll(SourceFixtureDirectory());
    }

    private static string SourceFixtureDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SkyCD.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(
            directory!.FullName,
            "tests",
            "SkyCD.Plugin.FileFormat.DiskCatalogMaker.Tests",
            DiskCatalogMakerV5Fixtures.FixtureDirectory);
    }
}
