using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class PluginMetadataTests
{
    [Fact]
    public void EveryFirstPartyPluginDeclaresHumanReadableTitleAndDescription()
    {
        var pluginsRoot = Path.Combine(FindRepositoryRoot(), "Plugins");
        var projectFiles = Directory
            .GetFiles(pluginsRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => Path.GetFileName(path).StartsWith("SkyCD.Plugin.", StringComparison.Ordinal))
            .Where(static path => !IsBuildOutput(path))
            .ToArray();

        Assert.NotEmpty(projectFiles);

        var failures = new List<string>();
        foreach (var projectFile in projectFiles)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFile);
            var document = XDocument.Load(projectFile);
            var title = ReadProperty(document, "AssemblyTitle");
            var description = ReadProperty(document, "Description");

            if (string.IsNullOrWhiteSpace(title))
            {
                failures.Add($"{projectName} must declare a non-empty <AssemblyTitle>.");
            }
            else if (title == projectName || title.StartsWith("SkyCD.Plugin.", StringComparison.Ordinal))
            {
                failures.Add($"{projectName} must declare a human-readable <AssemblyTitle>, not the assembly name.");
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                failures.Add($"{projectName} must declare a non-empty <Description>.");
            }
        }

        Assert.True(failures.Count == 0,
            "First-party plugin metadata is incomplete:" + Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    private static string? ReadProperty(XDocument document, string propertyName) =>
        document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == propertyName)?
            .Value.Trim();

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SkyCD.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Could not locate the repository root above '{AppContext.BaseDirectory}'.");
    }
}
