using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Ai.GenDesc.Configuration;
using SkyCD.Plugin.Ai.GenDesc.Exceptions;
using SkyCD.Plugin.Ai.GenDesc.Import;
using SkyCD.Plugin.Ai.GenDesc.Models;
using SkyCD.Plugin.Ai.GenDesc.Parsing;
using SkyCD.Plugin.Ai.GenDesc.Sources;

namespace SkyCD.Plugin.Ai.GenDesc;

/// <summary>
/// Reintroduces GenDesc.NET: it builds a media description from web sources rather than a model. On an explicit
/// generation pass it looks the entry up across the enabled sources (Wikipedia, IMDb, AniDB, Anime News Network,
/// TV.com), merges the facts, and renders a user-editable, site- and language-selected template into BBCode or
/// plain text. It presents itself as a selectable implementation so the existing Properties generate/preview/save
/// surface drives it, and the host's snapshot-freshness check protects the write.
/// </summary>
public sealed class GenDescPlugin : IGeneratedDescriptionCapability, IDescriptionGenerationPlanner, IAiDescriptionImplementationCapability
{
    /// <summary>The plugin version stamped into every description's provenance.</summary>
    public const string PluginVersion = "1.0.0";

    /// <summary>Stable implementation id shown in the Options picker; it must never change between releases.</summary>
    public const string ImplementationIdValue = "gendesc";

    private const string ProviderName = "GenDesc templates";
    private const string ProcessingDestination = "public-web";
    private const string Agent = "GenDesc";

    private static readonly DescriptionContextOptions ContextOptions =
        new(Filename: true, Metadata: true, Text: true, Images: false, Audio: false, MaxTextCharacters: 4000, MaxArtifactBytes: 0);

    private static readonly string[] KnownHosts =
        ["wikipedia.org", "imdb.com", "anidb.net", "anidb.info", "animenewsnetwork.com", "tv.com", "cbs.com"];

    private static readonly Regex UrlPattern = new("https?://[^\\s<>\"']+", RegexOptions.Compiled);

    private readonly GenDescSettingsStore store;
    private readonly WebSourceOrchestrator orchestrator;
    private readonly Func<DateTimeOffset> clock;

    public string ImplementationId => ImplementationIdValue;

    public string ImplementationName => ProviderName;

    public GenDescPlugin() : this(GenDescSettingsStore.DefaultConfigurationPath(), new HttpWebFetcher(), null)
    {
    }

    public GenDescPlugin(string configurationPath, IWebFetcher? fetcher, Func<DateTimeOffset>? clock = null)
    {
        store = new GenDescSettingsStore(configurationPath);
        orchestrator = new WebSourceOrchestrator(WebSourceCatalog.CreateAll(), fetcher ?? new HttpWebFetcher());
        this.clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    public MediaMetadataSchema Schema => new("skycd.ai-description.metadata", 1,
        new[]
        {
            "AiDescription", "AiDescriptionAgent", "AiDescriptionModel", "AiDescriptionPromptVersion",
            "AiDescriptionLanguage", "AiDescriptionOrigin", "AiDescriptionProcessingDestination",
            "AiDescriptionContextFingerprint", "AiDescriptionSourceFingerprint"
        }.Select(key => new MediaMetadataField(key, MediaMetadataType.Text)).ToArray());

    public async Task<GeneratedDescription> GenerateAsync(DescriptionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = store.Load();
        if (!settings.Enabled)
            throw new GenDescGenerationException("generation is disabled; enable GenDesc in configuration before generating");

        var entry = ReadEntry(context);
        var request = new ImportRequest(entry.Title, entry.Url, settings.Language);
        if (!request.HasLookup)
            throw new GenDescGenerationException("the entry carries no filename or source URL to look up");

        var timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        var run = await orchestrator.RunAsync(request, settings.Sources, timeout, cancellationToken).ConfigureAwait(false);

        var facts = run.Facts;
        facts.Force("Name", entry.Title);
        facts.Force("Filename", entry.Filename);
        if (!facts.Has("Title") && !string.IsNullOrWhiteSpace(entry.Title)) facts.Set("Title", entry.Title);

        var templates = LoadTemplates(settings);
        var template = templates.Select(ResolveSite(settings.Site, run), settings.Language, settings.TemplateId)
            ?? throw new GenDescGenerationException("no template is available for the configured site and language");

        var rendered = TemplateEngine.Render(template.Body, facts);
        var text = Bound(rendered, settings.MaxOutputCharacters);
        if (string.IsNullOrWhiteSpace(text))
            throw new GenDescGenerationException($"template '{template.Id}' rendered an empty description");

        return new GeneratedDescription(
            text,
            Agent,
            Model: null,
            PluginVersion,
            $"{template.Id}@{template.Version}",
            template.Language,
            Origin(facts),
            ProcessingDestination,
            clock());
    }

    /// <summary>
    /// Reports the locally configured provider without contacting any source, so the Properties view can show
    /// what GenDesc would use and why it may be unavailable before a user requests a generation pass.
    /// </summary>
    public DescriptionGenerationPlan GetPlan()
    {
        try
        {
            var settings = store.Load();
            if (!settings.Enabled)
                return new DescriptionGenerationPlan(false, ProviderName, PluginVersion, null, ProcessingDestination,
                    ContextOptions, "generation is disabled; create the GenDesc configuration and set \"enabled\": true to import from web sources");
            return new DescriptionGenerationPlan(true, ProviderName, PluginVersion, null, ProcessingDestination, ContextOptions);
        }
        catch (GenDescConfigurationException exception)
        {
            return new DescriptionGenerationPlan(false, ProviderName, PluginVersion, null, ProcessingDestination, ContextOptions, exception.Message);
        }
    }

    private TemplateStore LoadTemplates(GenDescSettings settings)
    {
        var directory = store.TemplateDirectory(settings);
        try
        {
            new TemplateStore(BuiltInTemplates.All()).SeedUserTemplates(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A read-only or unwritable folder still leaves the built-in templates selectable.
        }
        return TemplateStore.Load(directory);
    }

    private static string ResolveSite(string configuredSite, ImportRun run)
    {
        if (!string.IsNullOrWhiteSpace(configuredSite) &&
            !string.Equals(configuredSite, TemplateInfo.DefaultSite, StringComparison.OrdinalIgnoreCase)) return configuredSite;
        var primary = run.Results.FirstOrDefault(result => result.Succeeded && result.Facts.Keys.Count > 0);
        return primary?.SourceId ?? TemplateInfo.DefaultSite;
    }

    private static string Origin(DescriptionFacts facts) => facts.SourceUrls.Count > 0
        ? "web: " + string.Join(", ", facts.SourceUrls)
        : "template-only";

    private static string Bound(string value, int maxCharacters)
    {
        var text = value.Replace("\r\n", "\n").Trim();
        if (text.Length <= maxCharacters) return text;
        var cut = text[..maxCharacters];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > maxCharacters / 2) cut = cut[..lastSpace];
        return cut.TrimEnd() + "…";
    }

    private static EntryContext ReadEntry(DescriptionContext context)
    {
        string filename = string.Empty;
        string? url = null;
        foreach (var part in context.Parts)
        {
            if (part.Text is not { Length: > 0 } text) continue;
            if (string.Equals(part.Kind, "filename", StringComparison.OrdinalIgnoreCase) && filename.Length == 0)
            {
                filename = text;
                continue;
            }
            url ??= FindKnownUrl(text);
        }
        var title = filename.Length == 0 ? string.Empty : Path.GetFileNameWithoutExtension(filename).Trim();
        return new EntryContext(title, filename, url);
    }

    private static string? FindKnownUrl(string text)
    {
        foreach (Match match in UrlPattern.Matches(text))
        {
            if (KnownHosts.Any(host => UrlHelpers.MatchesHost(match.Value, host))) return match.Value;
        }
        return null;
    }

    private sealed record EntryContext(string Title, string Filename, string? Url);
}
