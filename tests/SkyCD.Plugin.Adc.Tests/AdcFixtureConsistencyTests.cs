using System;
using System.IO;
using Xunit;

namespace SkyCD.Plugin.Adc.Tests;

public sealed class AdcFixtureConsistencyTests
{
    [Fact]
    public void CommittedFixtures_MatchGenerator()
    {
        var expectedDirectory = Path.Combine(Path.GetTempPath(), "skycd-adc-fixtures-" + Guid.NewGuid().ToString("N"));
        try
        {
            AdcFixtures.WriteAll(expectedDirectory);
            foreach (var expectedPath in Directory.EnumerateFiles(expectedDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(expectedDirectory, expectedPath);
                var committedPath = AdcCheckedInFixtureTests.FixturePath(relative);
                if (relative == "manifest.json")
                {
                    Assert.Equal(
                        File.ReadAllText(expectedPath).Replace("\r\n", "\n", StringComparison.Ordinal),
                        File.ReadAllText(committedPath).Replace("\r\n", "\n", StringComparison.Ordinal));
                }
                else
                {
                    Assert.Equal(
                        File.ReadAllBytes(expectedPath),
                        File.ReadAllBytes(committedPath));
                }
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
                Environment.GetEnvironmentVariable("SKYCD_REGENERATE_ADC_FIXTURES"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        AdcFixtures.WriteAll(SourceFixtureDirectory());
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
            "SkyCD.Plugin.Adc.Tests",
            AdcFixtures.FixtureDirectory);
    }
}
