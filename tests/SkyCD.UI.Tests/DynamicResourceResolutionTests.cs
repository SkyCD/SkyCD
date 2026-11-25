using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issue #1135: an unresolved <c>DynamicResource</c> key fails silently and the property keeps its
/// default. This resolves every referenced key against the real application resources (SimpleTheme,
/// design tokens and the app styles) instead of comparing text, and reports every failing key with
/// the file that references it. Keys declared in the same file's resources (window resource
/// dictionaries, style dictionaries) resolve in that scope, matching Avalonia's lookup rules.
/// </summary>
public sealed class DynamicResourceResolutionTests
{
    private static readonly Regex XamlReference = new(
        "\\{DynamicResource\\s+(?<key>[A-Za-z0-9_][A-Za-z0-9_.]*)",
        RegexOptions.Compiled);

    private static readonly Regex CodeReference = new(
        "new DynamicResourceExtension\\(\"(?<key>[^\"]+)\"\\)",
        RegexOptions.Compiled);

    private static readonly Regex XamlDefinition = new(
        "x:Key=\"(?<key>[^\"]+)\"",
        RegexOptions.Compiled);

    /// <summary>The app follows the system theme, so a key must resolve in both shipped variants.</summary>
    private static readonly ThemeVariant[] ThemeVariants = [ThemeVariant.Light, ThemeVariant.Dark];

    [AvaloniaFact]
    public void EveryReferencedDynamicResourceKeyResolves()
    {
        var violations = new List<string>();
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            var declared = XamlDefinition.Matches(text)
                .Select(match => match.Groups["key"].Value)
                .ToHashSet(StringComparer.Ordinal);
            var references = file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                ? XamlReference.Matches(text).Select(match => match.Groups["key"].Value)
                : CodeReference.Matches(text).Select(match => match.Groups["key"].Value);

            violations.AddRange(references
                .Where(key => !declared.Contains(key))
                .Distinct(StringComparer.Ordinal)
                .SelectMany(key => ThemeVariants
                    .Where(variant => !Application.Current!.TryFindResource(key, variant, out _))
                    .Select(variant => $"{Relative(file)}: DynamicResource key '{key}' does not resolve for the {variant} theme")));
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> SourceFiles() =>
        new[] { "src", "Plugins" }
            .SelectMany(folder => Directory.EnumerateFiles(
                Path.Combine(FindRepoRoot(), folder), "*.*", SearchOption.AllDirectories))
            .Where(file => file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(file => !IsBuildOutput(file))
            .OrderBy(file => file, StringComparer.Ordinal);

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Relative(string fullPath) =>
        Path.GetRelativePath(FindRepoRoot(), fullPath).Replace('\\', '/');

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
