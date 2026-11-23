using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #898: windows and shared controls reference the design-token dictionary instead of
/// declaring literal font sizes and spacing values.
/// </summary>
public sealed class DesignTokenUsageTests
{
    private static readonly Regex NumericTypographyOrSpacing = new(
        "(FontSize|Spacing|RowSpacing|ColumnSpacing)=\"[0-9.]+\"",
        RegexOptions.Compiled);

    private static readonly Regex TokenReference = new(
        "DynamicResource (Sky[A-Za-z]+)",
        RegexOptions.Compiled);

    private static readonly Regex TokenDefinition = new(
        "x:Key=\"(Sky[A-Za-z]+)\"",
        RegexOptions.Compiled);

    [Fact]
    public void App_MergesDesignTokensAndGlobalStyles()
    {
        var app = ReadRepoFile("src", "SkyCD.App", "App.axaml");

        Assert.Contains("Styles/DesignTokens.axaml", app, StringComparison.Ordinal);
        Assert.Contains("Styles/GlobalStyles.axaml", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Xaml_DoesNotDeclareLiteralFontSizesOrSpacing()
    {
        var files = EnumerateXaml("src", "SkyCD.App")
            .Concat(EnumerateXaml("src", "SkyCD.UI"))
            .Concat(EnumerateXaml("Plugins"))
            .Where(path => !path.EndsWith("DesignTokens.axaml", StringComparison.OrdinalIgnoreCase));

        var violations = new List<string>();
        foreach (var file in files)
        {
            var matches = NumericTypographyOrSpacing.Matches(File.ReadAllText(file));
            violations.AddRange(matches.Select(match => $"{Relative(file)}: {match.Value}"));
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void EveryReferencedTokenIsDefined()
    {
        var defined = TokenDefinition.Matches(ReadRepoFile("src", "SkyCD.App", "Styles", "DesignTokens.axaml"))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = AllXaml()
            .SelectMany(file => TokenReference.Matches(File.ReadAllText(file))
                .Select(match => (File: file, Key: match.Groups[1].Value)))
            .Where(reference => !defined.Contains(reference.Key))
            .Select(reference => $"{Relative(reference.File)}: {reference.Key}")
            .Distinct()
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void About_UsesThemeBrushesInsteadOfHardCodedColors()
    {
        var about = ReadRepoFile("src", "SkyCD.App", "Views", "AboutWindow.axaml");

        Assert.DoesNotMatch("#[0-9A-Fa-f]{6,8}", about);
    }

    private static IEnumerable<string> AllXaml() =>
        EnumerateXaml("src", "SkyCD.App")
            .Concat(EnumerateXaml("src", "SkyCD.UI"))
            .Concat(EnumerateXaml("Plugins"));

    private static IEnumerable<string> EnumerateXaml(params string[] parts) =>
        Directory.EnumerateFiles(Path.Combine([FindRepoRoot(), .. parts]), "*.axaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Relative(string fullPath) =>
        Path.GetRelativePath(FindRepoRoot(), fullPath).Replace('\\', '/');

    private static string ReadRepoFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([FindRepoRoot(), .. parts]));

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SkyCD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root with SkyCD.slnx was not found.");
    }
}
