using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkyCD.Plugin.Runtime.Marketplace.Exceptions;

namespace SkyCD.Plugin.Runtime.Marketplace.Services;

/// <summary>
/// Shape of the two read-only NuGet v3 documents the catalog needs. Unknown members are ignored so newer
/// server fields never break parsing.
/// </summary>
internal static class NuGetFeedDocuments
{
    /// <summary>Relative type of the package content resource in a v3 service index.</summary>
    public const string PackageBaseAddressType = "PackageBaseAddress/3.0.0";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>Returns the absolute package content address, or <see langword="null"/> when absent.</summary>
    public static Uri? ParsePackageBaseAddress(Uri serviceIndexUri, string json)
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
            if (resource is null || !IsPackageBaseAddressResource(resource.Type))
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

        throw new MalformedNuGetServiceIndexException(serviceIndexUri,
            $"it exposes no {PackageBaseAddressType} resource");
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

    /// <summary>
    /// A service index may declare a resource type more than once and some servers add a versioned profile
    /// suffix (for example <c>PackageBaseAddress/3.0.0</c>). Both are accepted.
    /// </summary>
    private static bool IsPackageBaseAddressResource(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        return string.Equals(type, PackageBaseAddressType, StringComparison.OrdinalIgnoreCase) ||
               type.StartsWith(PackageBaseAddressType + "/", StringComparison.OrdinalIgnoreCase);
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
