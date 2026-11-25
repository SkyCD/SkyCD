using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.Ai.GenDesc.Models;

/// <summary>
/// User configuration for the GenDesc plugin, read from a JSON file under the SkyCD application data folder.
/// Generation stays off until explicitly enabled, so the plugin never performs network lookups without a user
/// action; the fields bound here also fix the template selection and network limits.
/// </summary>
public sealed record GenDescSettings
{
    /// <summary>Canonical source ids, in the default priority order the host runs them.</summary>
    public static readonly IReadOnlyList<string> KnownSources = ["wikipedia", "imdb", "anidb", "ann", "tvcom"];

    /// <summary>Explicit opt-in flag; network lookups only run once a user enables generation.</summary>
    public bool Enabled { get; init; }

    /// <summary>Preferred output language code (for example <c>en</c> or <c>lt</c>).</summary>
    public string Language { get; init; } = "en";

    /// <summary>Catalog site key used to pick a template; a source-specific override is resolved at runtime.</summary>
    public string Site { get; init; } = "default";

    /// <summary>Optional explicit template id; when null the store selects by site and language.</summary>
    public string? TemplateId { get; init; }

    /// <summary>Enabled sources in priority order; an empty list disables all network lookup.</summary>
    public IReadOnlyList<string> Sources { get; init; } = KnownSources;

    /// <summary>Upper bound on the rendered description length, clamped to the host's own 8000-character cap.</summary>
    public int MaxOutputCharacters { get; init; } = 4000;

    /// <summary>Per-source network timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 15;

    /// <summary>Optional override for the editable template directory; null uses the default location.</summary>
    public string? TemplateDirectory { get; init; }

    /// <summary>Throws when the settings cannot drive a safe, bounded generation pass.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Language) || Language.Length > 35)
            throw new Exceptions.GenDescConfigurationException("language must be a short non-empty code");
        if (string.IsNullOrWhiteSpace(Site) || Site.Length > 40)
            throw new Exceptions.GenDescConfigurationException("site must be a short non-empty key");
        if (TemplateId is { Length: > 100 })
            throw new Exceptions.GenDescConfigurationException("template id is too long");
        if (Sources.Any(string.IsNullOrWhiteSpace))
            throw new Exceptions.GenDescConfigurationException("source list contains a blank entry");
        if (MaxOutputCharacters is < 1 or > 8000)
            throw new Exceptions.GenDescConfigurationException("max output characters must be between 1 and 8000");
        if (TimeoutSeconds is < 1 or > 120)
            throw new Exceptions.GenDescConfigurationException("timeout seconds must be between 1 and 120");
        if (TemplateDirectory is not null && TemplateDirectory.Length > 500)
            throw new Exceptions.GenDescConfigurationException("template directory path is too long");
    }
}
