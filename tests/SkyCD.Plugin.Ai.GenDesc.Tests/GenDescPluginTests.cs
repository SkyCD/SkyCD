using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Ai.GenDesc.Configuration;
using SkyCD.Plugin.Ai.GenDesc.Exceptions;
using SkyCD.Plugin.Ai.GenDesc.Models;
using Xunit;

namespace SkyCD.Plugin.Ai.GenDesc.Tests;

public class GenDescPluginTests : IDisposable
{
    private static readonly DateTimeOffset FixedTime = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private readonly string workingDirectory;
    private readonly string configurationPath;
    private readonly string templateDirectory;

    public GenDescPluginTests()
    {
        workingDirectory = Path.Combine(Path.GetTempPath(), "gendesc-plugin-tests-" + Guid.NewGuid().ToString("N"));
        templateDirectory = Path.Combine(workingDirectory, "templates");
        configurationPath = Path.Combine(workingDirectory, "ai-gendesc.json");
        Directory.CreateDirectory(workingDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(workingDirectory, recursive: true); }
        catch (IOException) { }
    }

    private void WriteSettings(GenDescSettings settings) =>
        new GenDescSettingsStore(configurationPath).Save(settings);

    private static GenDescSettings Enabled(string templateDir, string language = "en", IReadOnlyList<string>? sources = null) => new()
    {
        Enabled = true,
        Language = language,
        Site = "default",
        Sources = sources ?? ["wikipedia"],
        TemplateDirectory = templateDir
    };

    private static DescriptionContext ContextWithFilename(string filename) => new(
        "entry-1", "source-fp", "context-fp", new DescriptionContextOptions(),
        [new DescriptionContextPart("filename", "text/plain", filename)]);

    private static DescriptionContext ContextWithNoLookup() => new(
        "entry-1", "source-fp", "context-fp", new DescriptionContextOptions(),
        [new DescriptionContextPart("metadata", "text/plain", "a bare note with no link")]);

    private static string WikipediaBody => Fixtures.Read("wikipedia-action.json");

    [Fact]
    public async Task GenerateThrowsWhenDisabled()
    {
        // No configuration file on disk => the safe default (disabled) is loaded.
        var plugin = new GenDescPlugin(configurationPath, new StubFetcher(_ => WikipediaBody), () => FixedTime);

        await Assert.ThrowsAsync<GenDescGenerationException>(() =>
            plugin.GenerateAsync(ContextWithFilename("Example Film.mkv"), CancellationToken.None));
    }

    [Fact]
    public async Task GenerateThrowsWhenEntryHasNoLookup()
    {
        WriteSettings(Enabled(templateDirectory));
        var plugin = new GenDescPlugin(configurationPath, new StubFetcher(_ => WikipediaBody), () => FixedTime);

        var exception = await Assert.ThrowsAsync<GenDescGenerationException>(() =>
            plugin.GenerateAsync(ContextWithNoLookup(), CancellationToken.None));
        Assert.Contains("no filename or source URL", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateRendersTemplateAndStampsProvenance()
    {
        WriteSettings(Enabled(templateDirectory));
        var fetcher = new StubFetcher(_ => WikipediaBody);
        var plugin = new GenDescPlugin(configurationPath, fetcher, () => FixedTime);

        var result = await plugin.GenerateAsync(ContextWithFilename("Example Film.mkv"), CancellationToken.None);

        Assert.Contains("Example Film", result.Text, StringComparison.Ordinal);
        Assert.Contains("directed by Jane Doe", result.Text, StringComparison.Ordinal);
        Assert.Equal("GenDesc", result.Agent);
        Assert.Null(result.Model);
        Assert.Equal(GenDescPlugin.PluginVersion, result.PluginVersion);
        Assert.Equal("default-english@1", result.PromptVersion);
        Assert.Equal("en", result.Language);
        Assert.Equal("public-web", result.ProcessingDestination);
        Assert.Equal(FixedTime, result.GeneratedAtUtc);
    }

    [Fact]
    public async Task OriginRecordsFetchedSourceUrls()
    {
        WriteSettings(Enabled(templateDirectory));
        var plugin = new GenDescPlugin(configurationPath, new StubFetcher(_ => WikipediaBody), () => FixedTime);

        var result = await plugin.GenerateAsync(ContextWithFilename("Example Film.mkv"), CancellationToken.None);

        Assert.StartsWith("web:", result.Origin, StringComparison.Ordinal);
        Assert.Contains("wikipedia.org", result.Origin, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OriginIsTemplateOnlyWhenEverySourceFails()
    {
        WriteSettings(Enabled(templateDirectory));
        // StubFetcher with a null body throws per source, so no source urls are recorded.
        var plugin = new GenDescPlugin(configurationPath, new StubFetcher(_ => null!), () => FixedTime);

        var result = await plugin.GenerateAsync(ContextWithFilename("Example Film.mkv"), CancellationToken.None);

        Assert.Equal("template-only", result.Origin);
        Assert.Contains("Example Film", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TemplateIdOverrideSelectsThatTemplate()
    {
        WriteSettings(new GenDescSettings
        {
            Enabled = true,
            Language = "en",
            Sources = ["wikipedia"],
            TemplateDirectory = templateDirectory,
            TemplateId = "default-lithuanian"
        });
        var plugin = new GenDescPlugin(configurationPath, new StubFetcher(_ => WikipediaBody), () => FixedTime);

        var result = await plugin.GenerateAsync(ContextWithFilename("Example Film.mkv"), CancellationToken.None);

        Assert.Equal("default-lithuanian@1", result.PromptVersion);
        Assert.Equal("lt", result.Language);
    }

    [Fact]
    public async Task LanguageSelectsMatchingBuiltInTemplate()
    {
        WriteSettings(Enabled(templateDirectory, language: "lt"));
        var plugin = new GenDescPlugin(configurationPath, new StubFetcher(_ => WikipediaBody), () => FixedTime);

        var result = await plugin.GenerateAsync(ContextWithFilename("Example Film.mkv"), CancellationToken.None);

        Assert.Equal("default-lithuanian@1", result.PromptVersion);
    }

    [Fact]
    public async Task OutputIsBoundToMaxCharacters()
    {
        WriteSettings(new GenDescSettings
        {
            Enabled = true,
            Language = "en",
            Sources = ["wikipedia"],
            TemplateDirectory = templateDirectory,
            MaxOutputCharacters = 25
        });
        var plugin = new GenDescPlugin(configurationPath, new StubFetcher(_ => WikipediaBody), () => FixedTime);

        var result = await plugin.GenerateAsync(ContextWithFilename("Example Film.mkv"), CancellationToken.None);

        Assert.True(result.Text.Length <= 26, $"expected a bound of 26, got {result.Text.Length}");
        Assert.EndsWith("…", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void GetPlanReportsDisabledWithReason()
    {
        var plugin = new GenDescPlugin(configurationPath, new StubFetcher(_ => WikipediaBody), () => FixedTime);

        var plan = plugin.GetPlan();

        Assert.False(plan.IsEnabled);
        Assert.Equal("GenDesc templates", plan.ProviderName);
        Assert.Equal(GenDescPlugin.PluginVersion, plan.ProviderVersion);
        Assert.Contains("disabled", plan.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetPlanReportsEnabled()
    {
        WriteSettings(Enabled(templateDirectory));
        var plugin = new GenDescPlugin(configurationPath, new StubFetcher(_ => WikipediaBody), () => FixedTime);

        var plan = plugin.GetPlan();

        Assert.True(plan.IsEnabled);
        Assert.Null(plan.UnavailableReason);
    }

    [Fact]
    public void GetPlanReportsConfigurationError()
    {
        // MaxOutputCharacters out of range makes Validate throw during Load.
        File.WriteAllText(configurationPath, "{ \"enabled\": true, \"maxOutputCharacters\": 99999 }");
        var plugin = new GenDescPlugin(configurationPath, new StubFetcher(_ => WikipediaBody), () => FixedTime);

        var plan = plugin.GetPlan();

        Assert.False(plan.IsEnabled);
        Assert.NotNull(plan.UnavailableReason);
    }

    [Fact]
    public void ExposesAStableImplementationId()
    {
        var plugin = new GenDescPlugin(configurationPath, new StubFetcher(_ => WikipediaBody));
        Assert.Equal("gendesc", plugin.ImplementationId);
        Assert.Equal("GenDesc templates", plugin.ImplementationName);
    }
}
