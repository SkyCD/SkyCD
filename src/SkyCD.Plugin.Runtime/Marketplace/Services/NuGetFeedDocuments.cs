using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Runtime.Marketplace.Exceptions;
using SkyCD.Plugin.Runtime.Marketplace.Models;

namespace SkyCD.Plugin.Runtime.Marketplace.Services;

/// <summary>
/// Reads the NuGet v3 metadata used to discover package versions and download locations.
/// </summary>
public static class NuGetFeedDocuments
{
    /// <summary>Relative type of the package content resource in a v3 service index.</summary>
    public const string PackageBaseAddressType = "PackageBaseAddress/3.0.0";
    private const int MaxExternalRegistrationPages = 16;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>Returns the absolute package content address, or <see langword="null"/> when absent.</summary>
    public static Uri? ParsePackageBaseAddress(Uri serviceIndexUri, string json)
        => ParseResource(serviceIndexUri, json, PackageBaseAddressType);

    /// <summary>Prefers registration metadata that supports SemVer 2.0 versions.</summary>
    public static Uri? ParseRegistrationBaseAddress(Uri serviceIndexUri, string json)
        => ParseResource(serviceIndexUri, json, "RegistrationsBaseUrl/3.6.0")
           ?? ParseResource(serviceIndexUri, json, "RegistrationsBaseUrl/3.4.0")
           ?? ParseResource(serviceIndexUri, json, "RegistrationsBaseUrl/3.0.0");

    private static Uri? ParseResource(Uri serviceIndexUri, string json, string resourceType)
    {
        ServiceIndex? index;
        try
        {
            index = JsonSerializer.Deserialize<ServiceIndex>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new MalformedNuGetServiceIndexException(serviceIndexUri, exception.Message);
        }

        if (index?.Resources is null)
        {
            throw new MalformedNuGetServiceIndexException(serviceIndexUri, "it lists no resources");
        }

        foreach (var resource in index.Resources)
        {
            if (resource?.Type is not { } type ||
                (!string.Equals(type, resourceType, StringComparison.OrdinalIgnoreCase) &&
                 !type.StartsWith(resourceType + "/", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var id = resource.Id;
            if (!Uri.TryCreate(id, UriKind.Absolute, out var address) ||
                !string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(address.UserInfo))
            {
                throw new PluginSourceProtocolException(address ?? serviceIndexUri);
            }

            return address;
        }

        return null;
    }

    /// <summary>Reads exact-version package URLs from a registration index and its external pages.</summary>
    public static async Task<Dictionary<string, Uri>> ReadRegistrationAsync(Uri registrationBase, string packageId,
        Func<Uri, CancellationToken, Task<string>> fetch, CancellationToken cancellationToken)
    {
        var root = registrationBase.AbsoluteUri.EndsWith('/')
            ? registrationBase
            : new Uri(registrationBase.AbsoluteUri + "/");
        var id = Uri.EscapeDataString(packageId.ToLowerInvariant());
        var index = new Uri(root, $"{id}/index.json");
        using var document = JsonDocument.Parse(await fetch(index, cancellationToken).ConfigureAwait(false));
        var versions = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        var externalPages = new HashSet<Uri>();
        foreach (var page in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (page.TryGetProperty("items", out var entries))
            {
                ReadRegistrationEntries(entries, packageId, versions);
                continue;
            }

            var pageUri = ReadHttpsUri(page.GetProperty("@id"));
            if (!externalPages.Add(pageUri) || externalPages.Count > MaxExternalRegistrationPages)
            {
                throw new JsonException("The registration index has duplicate or excessive external pages.");
            }

            using var external = JsonDocument.Parse(await fetch(pageUri, cancellationToken).ConfigureAwait(false));
            ReadRegistrationEntries(external.RootElement.GetProperty("items"), packageId, versions);
        }

        return versions;
    }

    private static void ReadRegistrationEntries(JsonElement entries, string packageId, Dictionary<string, Uri> versions)
    {
        foreach (var entry in entries.EnumerateArray())
        {
            var metadata = entry.GetProperty("catalogEntry");
            if (!string.Equals(metadata.GetProperty("id").GetString(), packageId, StringComparison.OrdinalIgnoreCase) ||
                !PluginPackageVersion.TryParse(metadata.GetProperty("version").GetString(), out var version) ||
                !versions.TryAdd(version!.Normalized, ReadHttpsUri(entry.GetProperty("packageContent"))))
            {
                throw new JsonException("The registration page has a mismatched package ID or invalid release version.");
            }
        }
    }

    private static Uri ReadHttpsUri(JsonElement element)
    {
        if (!Uri.TryCreate(element.GetString(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new JsonException("Registration URLs must use HTTPS and omit URL credentials.");
        }

        return uri;
    }

    /// <summary>
    /// Reads the versions a package content resource lists. The spec returns an object with a
    /// <c>versions</c> array; a bare array is accepted too because real v3 servers emit both shapes.
    /// </summary>
    public static IReadOnlyList<string> ParseVersions(Uri versionListUri, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("versions", out var nested))
            {
                root = nested;
            }

            if (root.ValueKind != JsonValueKind.Array)
            {
                throw new MalformedNuGetVersionListException(versionListUri);
            }

            var versions = new List<string>();
            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
                {
                    throw new MalformedNuGetVersionListException(versionListUri);
                }

                versions.Add(element.GetString()!);
            }

            return versions;
        }
        catch (JsonException)
        {
            throw new MalformedNuGetVersionListException(versionListUri);
        }
    }

    private sealed class ServiceIndex
    {
        [JsonPropertyName("resources")]
        public List<ServiceResource?>? Resources { get; set; }
    }

    private sealed class ServiceResource
    {
        [JsonPropertyName("@id")]
        public string? Id { get; set; }

        [JsonPropertyName("@type")]
        public string? Type { get; set; }
    }
}
