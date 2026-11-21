using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Accounts.JetBrains.Models;

/// <summary>JetBrains product codes, display names and IDE executable names.</summary>
public static class JetBrainsProducts
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["II"] = "IntelliJ IDEA Ultimate",
        ["IIC"] = "IntelliJ IDEA Community",
        ["PS"] = "PhpStorm",
        ["PC"] = "PyCharm",
        ["WS"] = "WebStorm",
        ["GO"] = "GoLand",
        ["RM"] = "RubyMine",
        ["CL"] = "CLion",
        ["DG"] = "DataGrip",
        ["DS"] = "DataSpell",
        ["RD"] = "Rider",
        ["RS"] = "ReSharper",
        ["RSC"] = "ReSharper C++",
        ["DC"] = "dotCover",
        ["DM"] = "dotMemory",
        ["DP"] = "dotPeek",
        ["TC"] = "TeamCity",
        ["YT"] = "YouTrack",
        ["TBA"] = "Toolbox App",
        ["ALL"] = "All Products Pack"
    };

    private static readonly Dictionary<string, string> ExecutableCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["idea64.exe"] = "II",
        ["idea.exe"] = "II",
        ["idea"] = "II",
        ["idea.sh"] = "II",
        ["phpstorm64.exe"] = "PS",
        ["phpstorm.exe"] = "PS",
        ["phpstorm"] = "PS",
        ["phpstorm.sh"] = "PS",
        ["pycharm64.exe"] = "PC",
        ["pycharm.exe"] = "PC",
        ["pycharm"] = "PC",
        ["pycharm.sh"] = "PC",
        ["webstorm64.exe"] = "WS",
        ["webstorm.exe"] = "WS",
        ["webstorm"] = "WS",
        ["webstorm.sh"] = "WS",
        ["goland64.exe"] = "GO",
        ["goland.exe"] = "GO",
        ["goland"] = "GO",
        ["goland.sh"] = "GO",
        ["rubymine64.exe"] = "RM",
        ["rubymine.exe"] = "RM",
        ["rubymine"] = "RM",
        ["rubymine.sh"] = "RM",
        ["clion64.exe"] = "CL",
        ["clion.exe"] = "CL",
        ["clion"] = "CL",
        ["clion.sh"] = "CL",
        ["datagrip64.exe"] = "DG",
        ["datagrip.exe"] = "DG",
        ["datagrip"] = "DG",
        ["datagrip.sh"] = "DG",
        ["dataspell64.exe"] = "DS",
        ["dataspell.exe"] = "DS",
        ["dataspell"] = "DS",
        ["dataspell.sh"] = "DS",
        ["rider64.exe"] = "RD",
        ["rider.exe"] = "RD",
        ["rider"] = "RD",
        ["rider.sh"] = "RD"
    };

    /// <summary>Gets the license product code to display name map.</summary>
    public static IReadOnlyDictionary<string, string> DisplayNames => Names;

    /// <summary>Resolves a product code to its display name, preserving unknown codes.</summary>
    public static string ResolveName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "Unknown product";
        }

        var trimmed = code.Trim();
        return Names.TryGetValue(trimmed, out var name) ? name : trimmed;
    }

    /// <summary>Resolves an IDE executable file name to a product code.</summary>
    public static bool TryGetProductCodeForExecutable(string fileName, out string productCode)
    {
        if (ExecutableCodes.TryGetValue(fileName, out var code))
        {
            productCode = code;
            return true;
        }

        productCode = string.Empty;
        return false;
    }

    /// <summary>
    /// Returns whether an installed IDE code represents the same IDE as a licensed product code.
    /// IntelliJ IDEA Ultimate and Community share one executable and cannot be told apart from disk.
    /// </summary>
    public static bool MatchesInstalledIde(string installedProductCode, string licenseProductCode)
    {
        if (string.Equals(installedProductCode, licenseProductCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsIntelliJ(installedProductCode) && IsIntelliJ(licenseProductCode);
    }

    private static bool IsIntelliJ(string code) =>
        code.Equals("II", StringComparison.OrdinalIgnoreCase)
        || code.Equals("IIC", StringComparison.OrdinalIgnoreCase);
}
