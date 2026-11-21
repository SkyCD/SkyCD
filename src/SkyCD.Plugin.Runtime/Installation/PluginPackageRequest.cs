namespace SkyCD.Plugin.Runtime.Installation;

/// <summary>
/// Identifies the exact package that should be staged: NuGet v3 service index URL, package id and version.
/// </summary>
/// <param name="serviceIndexUrl">HTTPS URL of the NuGet v3 service index that exposes <c>PackageBaseAddress/3.0.0</c>.</param>
/// <param name="packageId">Package id as selected by the catalog.</param>
/// <param name="version">Exact package version, for example <c>1.2.3</c>; version comments are ignored.</param>
public sealed record PluginPackageRequest(string serviceIndexUrl, string packageId, string version)
{
    /// <summary>Package id with comments removed and surrounding whitespace trimmed.</summary>
    public string NormalizedPackageId => PackageIdentity.Normalize(Normalize(packageId));

    /// <summary>Package version with comments removed and surrounding whitespace trimmed.</summary>
    public string NormalizedVersion => PackageIdentity.Normalize(Normalize(version));

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var commentStart = value.IndexOf('(');
        var withoutComment = commentStart >= 0 ? value[..commentStart] : value;
        return withoutComment.Trim();
    }
}
