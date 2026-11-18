using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SkyCD.Plugin.Accounts.Microsoft365;

/// <summary>Microsoft 365 SKU part numbers, Office applications and desktop suite names.</summary>
public static class Microsoft365Products
{
    private static readonly Dictionary<string, string> SubscriptionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["O365_BUSINESS_ESSENTIALS"] = "Microsoft 365 Business Basic",
        ["O365_BUSINESS_PREMIUM"] = "Microsoft 365 Business Standard",
        ["O365_BUSINESS"] = "Microsoft 365 Apps for business",
        ["SPB"] = "Microsoft 365 Business Premium",
        ["SPE_E3"] = "Microsoft 365 E3",
        ["SPE_E5"] = "Microsoft 365 E5",
        ["SPE_F1"] = "Microsoft 365 F1",
        ["SPE_F3"] = "Microsoft 365 F3",
        ["ENTERPRISEPACK"] = "Office 365 E3",
        ["ENTERPRISEPREMIUM"] = "Office 365 E5",
        ["STANDARDPACK"] = "Office 365 E1",
        ["EXCHANGESTANDARD"] = "Exchange Online (Plan 1)",
        ["EXCHANGEENTERPRISE"] = "Exchange Online (Plan 2)",
        ["FLOW_FREE"] = "Power Automate Free",
        ["POWER_BI_STANDARD"] = "Power BI",
        ["TEAMS_EXPLORATORY"] = "Teams Exploratory",
        ["MCOSTANDARD"] = "Skype for Business Online",
        ["VISIOCLIENT"] = "Visio Plan 2",
        ["VISIOONLINE_PLAN1"] = "Visio Plan 1",
        ["PROJECTPROFESSIONAL"] = "Project Plan 3",
        ["PROJECTPREMIUM"] = "Project Plan 5",
        ["PROJECTESSENTIALS"] = "Project Plan 1"
    };

    private static readonly Dictionary<string, string> OfficeAppNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WINWORD"] = "Word",
        ["EXCEL"] = "Excel",
        ["POWERPNT"] = "PowerPoint",
        ["OUTLOOK"] = "Outlook",
        ["MSACCESS"] = "Access",
        ["ONENOTE"] = "OneNote",
        ["MSPUB"] = "Publisher",
        ["VISIO"] = "Visio",
        ["WINPROJ"] = "Project"
    };

    private static readonly Dictionary<string, string> ExecutableCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["winword.exe"] = "WINWORD",
        ["excel.exe"] = "EXCEL",
        ["powerpnt.exe"] = "POWERPNT",
        ["outlook.exe"] = "OUTLOOK",
        ["msaccess.exe"] = "MSACCESS",
        ["onenote.exe"] = "ONENOTE",
        ["mspub.exe"] = "MSPUB",
        ["visio.exe"] = "VISIO",
        ["winproj.exe"] = "WINPROJ"
    };

    private static readonly Dictionary<string, string> PackageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["O365ProPlusRetail"] = "Microsoft 365 Apps for enterprise",
        ["O365BusinessRetail"] = "Microsoft 365 Apps for business",
        ["O365HomePremRetail"] = "Microsoft 365 Family",
        ["O365PersonalRetail"] = "Microsoft 365 Personal",
        ["O365SmallBusPremRetail"] = "Microsoft 365 Business Premium",
        ["O365EduCloudRetail"] = "Microsoft 365 Apps for students",
        ["MondoRetail"] = "Microsoft 365 Enterprise",
        ["ProfessionalPlus2019Retail"] = "Office Professional Plus 2019",
        ["ProPlus2019Retail"] = "Office Professional Plus 2019",
        ["ProfessionalPlus2021Retail"] = "Office Professional Plus 2021",
        ["ProPlus2021Retail"] = "Office Professional Plus 2021",
        ["Standard2019Retail"] = "Office Standard 2019",
        ["Standard2021Retail"] = "Office Standard 2021",
        ["HomeBusiness2019Retail"] = "Office Home & Business 2019",
        ["HomeBusiness2021Retail"] = "Office Home & Business 2021",
        ["HomeStudent2019Retail"] = "Office Home & Student 2019",
        ["HomeStudent2021Retail"] = "Office Home & Student 2021",
        ["Personal2019Retail"] = "Office Personal 2019",
        ["Personal2021Retail"] = "Office Personal 2021"
    };

    private static readonly IReadOnlyList<string> AllAppCodes = OfficeAppNames.Keys.ToArray();

    /// <summary>Gets the SKU part number to display name map.</summary>
    public static IReadOnlyDictionary<string, string> DisplayNames => SubscriptionNames;

    /// <summary>Gets the installed Office application codes.</summary>
    public static IReadOnlyList<string> AppCodes => AllAppCodes;

    /// <summary>Resolves a SKU part number to its display name, preserving unknown codes.</summary>
    public static string ResolveName(string? skuPartNumber)
    {
        if (string.IsNullOrWhiteSpace(skuPartNumber))
        {
            return "Unknown plan";
        }

        var trimmed = skuPartNumber.Trim();
        return SubscriptionNames.TryGetValue(trimmed, out var name) ? name : trimmed;
    }

    /// <summary>Resolves a Click-to-Run release ID to its product name, preserving unknown IDs.</summary>
    public static string ResolvePackageName(string? releaseId)
    {
        if (string.IsNullOrWhiteSpace(releaseId))
        {
            return string.Empty;
        }

        var trimmed = releaseId.Trim();
        return PackageNames.TryGetValue(trimmed, out var name) ? name : trimmed;
    }

    /// <summary>Resolves an Office application code or executable name to its application code.</summary>
    public static bool TryResolveAppCode(string? value, out string productCode)
    {
        productCode = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim().Trim('"');
        if (OfficeAppNames.ContainsKey(trimmed))
        {
            productCode = trimmed.ToUpperInvariant();
            return true;
        }

        var fileName = Path.GetFileName(trimmed);
        if (ExecutableCodes.TryGetValue(fileName, out var code))
        {
            productCode = code;
            return true;
        }

        if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && OfficeAppNames.ContainsKey(fileName[..^4]))
        {
            productCode = fileName[..^4].ToUpperInvariant();
            return true;
        }

        return false;
    }

    /// <summary>Resolves an Office application code to its display name.</summary>
    public static string ResolveAppName(string? productCode)
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return "Unknown Office app";
        }

        var trimmed = productCode.Trim();
        return OfficeAppNames.TryGetValue(trimmed, out var name) ? name : trimmed;
    }

    /// <summary>
    /// Returns the installed application codes a subscription entitles. Suite subscriptions
    /// link every detected Office app; Visio and Project plans link only their own app.
    /// </summary>
    public static IReadOnlyList<string> GetLinkedAppCodes(string? skuPartNumber)
    {
        if (string.IsNullOrWhiteSpace(skuPartNumber))
        {
            return [];
        }

        var name = ResolveName(skuPartNumber);
        if (name.Contains("Visio", StringComparison.OrdinalIgnoreCase))
        {
            return ["VISIO"];
        }

        if (name.Contains("Project", StringComparison.OrdinalIgnoreCase))
        {
            return ["WINPROJ"];
        }

        if (name.Contains("Microsoft 365", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Office 365", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Office Professional", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Office Standard", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Office Home", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Office Personal", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Microsoft Apps", StringComparison.OrdinalIgnoreCase))
        {
            return AllAppCodes;
        }

        return [];
    }
}
