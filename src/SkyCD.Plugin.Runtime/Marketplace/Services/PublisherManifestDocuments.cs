using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using SkyCD.Plugin.Runtime.Marketplace.Models;

namespace SkyCD.Plugin.Runtime.Marketplace.Services;

/// <summary>A publisher manifest (schema version 1) as read, before availability is checked.</summary>
internal sealed record ParsedPublisherManifest(
    int SchemaVersion,
    string? Publisher,
    string? ProjectUrl,
    string FeedText,
    IReadOnlyList<ParsedManifestPackage> Packages);

internal sealed record ParsedManifestPackage(
    string Id,
    string? DisplayName,
    string? Summary,
    string? License,
    string? ProjectUrl,
    IReadOnlyList<ParsedManifestRelease> Releases);

internal sealed record ParsedManifestRelease(
    PluginPackageVersion Version,
    Version MinHostVersion,
    Version? MaxHostVersion,
    IReadOnlyList<string> Platforms);

internal enum ManifestReadStatus
{
    Parsed,
    UnsupportedSchema,
    Malformed
}

internal sealed record ManifestReadResult(
    ManifestReadStatus Status,
    int SchemaVersion,
    string Reason,
    ParsedPublisherManifest? Manifest);

/// <summary>
/// Reader for publisher manifest schema version 1. Only <c>schemaVersion</c> is judged before the rest of the
/// document, so a newer schema is reported as unsupported instead of as malformed, and a manifest is either
/// accepted whole or rejected whole - one bad release never silently disappears.
/// </summary>
internal static class PublisherManifestDocuments
{
    public const int SupportedSchemaVersion = 1;

    public static ManifestReadResult Read(string json, int maxPackages, int maxReleases)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Malformed("the document is empty");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return Malformed(exception.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Malformed("the root is not an object");
            }

            if (!root.TryGetProperty("schemaVersion", out var schemaElement) ||
                schemaElement.ValueKind != JsonValueKind.Number ||
                !schemaElement.TryGetInt32(out var schemaVersion))
            {
                return Malformed("schemaVersion must be an integer");
            }

            if (schemaVersion != SupportedSchemaVersion)
            {
                return new ManifestReadResult(ManifestReadStatus.UnsupportedSchema, schemaVersion,
                    $"schema version {schemaVersion} is not supported", null);
            }

            if (!TryReadString(root, "feed", required: true, out var feedText, out var reason))
            {
                return Malformed(reason);
            }

            if (!root.TryGetProperty("packages", out var packagesElement) ||
                packagesElement.ValueKind != JsonValueKind.Array)
            {
                return Malformed("packages must be an array");
            }

            if (packagesElement.GetArrayLength() > maxPackages)
            {
                return Malformed($"the manifest lists more than {maxPackages} packages");
            }

            if (!TryReadOptionalString(root, "publisher", out var publisher, out reason) ||
                !TryReadOptionalString(root, "projectUrl", out var projectUrl, out reason))
            {
                return Malformed(reason);
            }

            var seenPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var packages = new List<ParsedManifestPackage>();
            foreach (var packageElement in packagesElement.EnumerateArray())
            {
                if (packageElement.ValueKind != JsonValueKind.Object)
                {
                    return Malformed("every package must be an object");
                }

                if (!TryReadString(packageElement, "id", required: true, out var id, out reason))
                {
                    return Malformed(reason);
                }

                if (!seenPackages.Add(id))
                {
                    return Malformed($"package '{id}' is listed twice");
                }

                if (!TryReadOptionalString(packageElement, "displayName", out var displayName, out reason) ||
                    !TryReadOptionalString(packageElement, "summary", out var summary, out reason) ||
                    !TryReadOptionalString(packageElement, "license", out var license, out reason) ||
                    !TryReadOptionalString(packageElement, "projectUrl", out var packageProjectUrl, out reason))
                {
                    return Malformed($"package '{id}': {reason}");
                }

                if (!packageElement.TryGetProperty("releases", out var releasesElement) ||
                    releasesElement.ValueKind != JsonValueKind.Array)
                {
                    return Malformed($"package '{id}': releases must be an array");
                }

                if (releasesElement.GetArrayLength() > maxReleases)
                {
                    return Malformed($"package '{id}' lists more than {maxReleases} releases");
                }

                var seenVersions = new HashSet<string>(StringComparer.Ordinal);
                var releases = new List<ParsedManifestRelease>();
                foreach (var releaseElement in releasesElement.EnumerateArray())
                {
                    var release = ReadRelease(id, releaseElement, seenVersions, out reason);
                    if (release is null)
                    {
                        return Malformed(reason);
                    }

                    releases.Add(release);
                }

                packages.Add(new ParsedManifestPackage(id, displayName, summary, license, packageProjectUrl, releases));
            }

            return new ManifestReadResult(ManifestReadStatus.Parsed, schemaVersion, string.Empty,
                new ParsedPublisherManifest(schemaVersion, publisher, projectUrl, feedText, packages));
        }
    }

    /// <summary>Parses a three-part host version such as <c>3.0.0</c>; prerelease labels are ignored.</summary>
    internal static bool TryParseVersion(string? text, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split('-', 2);
        var numeric = parts[0].Split('.');
        if (numeric.Length is < 1 or > 4)
        {
            return false;
        }

        var values = new int[3];
        for (var index = 0; index < numeric.Length; index++)
        {
            if (!int.TryParse(numeric[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            if (index < 3)
            {
                values[index] = value;
            }
        }

        version = new Version(values[0], values[1], values[2]);
        return true;
    }

    /// <summary>Accepts only absolute HTTPS URLs without credentials.</summary>
    internal static bool TryParseHttpsUri(string? text, out Uri? uri)
    {
        uri = null;
        return !string.IsNullOrWhiteSpace(text) &&
               Uri.TryCreate(text.Trim(), UriKind.Absolute, out uri) &&
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               string.IsNullOrEmpty(uri.UserInfo);
    }

    private static ParsedManifestRelease? ReadRelease(string packageId, JsonElement element,
        HashSet<string> seenVersions, out string reason)
    {
        reason = string.Empty;
        if (element.ValueKind != JsonValueKind.Object)
        {
            reason = $"package '{packageId}': every release must be an object";
            return null;
        }

        if (!TryReadString(element, "version", required: true, out var versionText, out reason))
        {
            reason = $"package '{packageId}': {reason}";
            return null;
        }

        if (!PluginPackageVersion.TryParse(versionText, out var version))
        {
            reason = $"package '{packageId}': '{versionText}' is not a valid version";
            return null;
        }

        if (!seenVersions.Add(version!.Normalized))
        {
            reason = $"package '{packageId}': version '{versionText}' is listed twice";
            return null;
        }

        if (!TryReadString(element, "minHostVersion", required: true, out var minText, out _) ||
            !TryParseVersion(minText, out var minHostVersion))
        {
            reason = $"package '{packageId}': minHostVersion is missing or invalid";
            return null;
        }

        if (!TryReadOptionalString(element, "maxHostVersion", out var maxText, out _))
        {
            reason = $"package '{packageId}': maxHostVersion must be a string";
            return null;
        }

        Version? maxHostVersion = null;
        if (maxText is not null)
        {
            if (!TryParseVersion(maxText, out maxHostVersion))
            {
                reason = $"package '{packageId}': maxHostVersion '{maxText}' is invalid";
                return null;
            }

            if (maxHostVersion < minHostVersion)
            {
                reason = $"package '{packageId}': maxHostVersion is lower than minHostVersion";
                return null;
            }
        }

        var platforms = new List<string>();
        if (element.TryGetProperty("platforms", out var platformsElement) &&
            platformsElement.ValueKind != JsonValueKind.Null)
        {
            if (platformsElement.ValueKind != JsonValueKind.Array)
            {
                reason = $"package '{packageId}': platforms must be an array";
                return null;
            }

            foreach (var platformElement in platformsElement.EnumerateArray())
            {
                if (platformElement.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(platformElement.GetString()))
                {
                    reason = $"package '{packageId}': every platform must be a non-empty string";
                    return null;
                }

                var platform = platformElement.GetString()!.Trim();
                if (!platforms.Contains(platform, StringComparer.OrdinalIgnoreCase))
                {
                    platforms.Add(platform);
                }
            }
        }

        return new ParsedManifestRelease(version, minHostVersion!, maxHostVersion, platforms);
    }

    private static bool TryReadString(JsonElement element, string name, bool required, out string value,
        out string reason)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property))
        {
            reason = $"'{name}' is required";
            return !required;
        }

        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            reason = $"'{name}' must be a non-empty string";
            return false;
        }

        value = property.GetString()!.Trim();
        reason = string.Empty;
        return true;
    }

    private static bool TryReadOptionalString(JsonElement element, string name, out string? value, out string reason)
    {
        value = null;
        reason = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            reason = $"'{name}' must be a string";
            return false;
        }

        value = string.IsNullOrWhiteSpace(property.GetString()) ? null : property.GetString()!.Trim();
        return true;
    }

    private static ManifestReadResult Malformed(string reason)
    {
        return new ManifestReadResult(ManifestReadStatus.Malformed, SupportedSchemaVersion, reason, null);
    }
}
