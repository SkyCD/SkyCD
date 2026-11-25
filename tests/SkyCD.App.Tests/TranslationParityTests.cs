using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1130: only the English catalog was verified, so Lithuanian lost keys silently and the
/// fallback rendered English text. These tests compare every shipped catalog pair and every
/// TextKeys/UiKeys constant against both cultures, file-based like <see cref="DesignTokenUsageTests"/>.
/// </summary>
public sealed class TranslationParityTests
{
    private static readonly Regex CatalogEntry = new(
        "^\\s*(?<key>[A-Za-z0-9_][A-Za-z0-9_.\\-]*):\\s*(?<value>.*)$",
        RegexOptions.Compiled);

    [Fact]
    public void LithuanianCatalog_HasTheSameKeysAsEnglishCatalog()
    {
        var violations = new List<string>();
        foreach (var englishFile in TranslationFiles("en"))
        {
            var suffix = Path.GetFileName(englishFile)[2..];
            var lithuanianFile = Path.Combine(Path.GetDirectoryName(englishFile)!, "lt" + suffix);
            if (!File.Exists(lithuanianFile))
            {
                violations.Add($"{Relative(englishFile)}: no Lithuanian counterpart");
                continue;
            }

            var english = ReadEntries(englishFile);
            var lithuanian = ReadEntries(lithuanianFile);
            violations.AddRange(english.Keys
                .Where(key => !lithuanian.ContainsKey(key))
                .Select(key => $"{Relative(englishFile)}: '{key}' is missing from {Path.GetFileName(lithuanianFile)}"));
            violations.AddRange(lithuanian.Keys
                .Where(key => !english.ContainsKey(key))
                .Select(key => $"{Relative(lithuanianFile)}: '{key}' exists only in Lithuanian and is never shown"));
            violations.AddRange(lithuanian
                .Where(pair => string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{Relative(lithuanianFile)}: '{pair.Key}' has no Lithuanian text"));
        }

        violations.AddRange(TranslationFiles("lt")
            .Where(file => !File.Exists(Path.Combine(Path.GetDirectoryName(file)!, "en" + Path.GetFileName(file)[2..])))
            .Select(file => $"{Relative(file)}: no English counterpart"));
        Assert.Empty(violations);
    }

    [Fact]
    public void KeyConstants_AreCoveredByBothShippedCatalogs()
    {
        var english = AllEntries("en");
        var lithuanian = AllEntries("lt");
        var violations = new List<string>();
        foreach (var (type, field, key) in ConstantKeys())
        {
            var catalogKey = key[(key.IndexOf('.') + 1)..];
            if (!english.ContainsKey(catalogKey))
            {
                violations.Add($"{type.Name}.{field}: '{key}' has no English catalog entry");
            }

            if (!lithuanian.ContainsKey(catalogKey))
            {
                violations.Add($"{type.Name}.{field}: '{key}' has no Lithuanian catalog entry");
            }
        }

        Assert.Empty(violations);
    }

    private static IEnumerable<(Type Type, string Field, string Key)> ConstantKeys() =>
        new[] { typeof(UiKeys), typeof(TextKeys) }
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (type, field.Name, Assert.IsType<string>(field.GetValue(null)))));

    private static Dictionary<string, string> AllEntries(string culture) =>
        TranslationFiles(culture)
            .SelectMany(ReadEntries)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static Dictionary<string, string> ReadEntries(string path)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            var match = CatalogEntry.Match(line);
            if (match.Success)
            {
                entries[match.Groups["key"].Value] = match.Groups["value"].Value;
            }
        }

        return entries;
    }

    private static IEnumerable<string> TranslationFiles(string culture) =>
        Directory.EnumerateFiles(Path.Combine(FindRepoRoot(), "src", "SkyCD.UI", "Translations"), culture + "-*.yaml")
            .OrderBy(path => path, StringComparer.Ordinal);

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
