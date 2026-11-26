using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.Plugin.Metadata.Html.Models;

namespace SkyCD.Plugin.Metadata.Html.Tests;

/// <summary>
/// End-to-end coverage for the HTML plugin inside the host pipeline: provider selection, host resource
/// budgets, failure isolation, and assembly discovery without a manifest.
/// </summary>
public sealed class HtmlPluginIntegrationTests
{
    private const string PluginId = "SkyCD.Plugin.Html.Metadata";

    [Fact]
    public async Task Host_ExtractsMetadataForASingleDocument()
    {
        var source = HtmlFixtures.Text(
            "<html><head><title>Integration</title><meta name=\"author\" content=\"A. Writer\"></head><body><h1>Heading</h1><p>Body paragraph.</p></body></html>");

        var results = await CreateManager().ProcessAsync(
            new MediaItem(".html", "text/html", source.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(source)));

        var metadata = Assert.Single(results, result => result.Schema?.Id == "skycd.html.metadata");
        Assert.False(metadata.Failed);
        Assert.Equal("skycd.html.metadata", metadata.Schema!.Id);
        Assert.Equal("Integration", metadata.Metadata!.Values.Single(value => value.Key == HtmlSchema.Title).Text);
        Assert.Equal("A. Writer",
            metadata.Metadata!.Values.Single(value => value.Key == HtmlSchema.Author).Text);
    }

    [Fact]
    public void Host_ClaimsOnlyHtmlSources()
    {
        var manager = CreateManager();

        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".html", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".HTM", "text/html", 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".htm", "text/html", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".md", "text/markdown", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".txt", "text/plain", 10)));
    }

    [Fact]
    public async Task UndecodableDocument_IsReportedAsAFailedProviderInsteadOfBreakingThePass()
    {
        var source = HtmlFixtures.WithNullByte();
        var results = await CreateManager().ProcessAsync(
            new MediaItem(".html", "text/html", source.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(source)));

        var result = Assert.Single(results,
            candidate => candidate.Capability.EndsWith("HtmlMetadataPlugin", StringComparison.Ordinal) &&
                         candidate.Failed);
        Assert.True(result.Failed);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task HostSourceBudget_RejectsADocumentLargerThanTheBudget()
    {
        var source = HtmlFixtures.Text("<html><body><p>Larger than the budget</p></body></html>");
        var manager = CreateManager(new MediaExecutionLimits(SourceBytes: 16));

        var results = await manager.ProcessAsync(
            new MediaItem(".html", "text/html", source.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(source)));

        Assert.All(results, result => Assert.True(result.Failed));
    }

    [Fact]
    public async Task Host_PublishesMetadataAndARenderedPageForADocumentWithText()
    {
        var source = HtmlFixtures.Text(
            "<html><head><title>Rendering</title></head><body><h1>Heading</h1><p>Body paragraph.</p><h2>Section</h2><p>More body.</p></body></html>");

        var results = await CreateManager().ProcessAsync(
            new MediaItem(".html", "text/html", source.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(source)));

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.False(result.Failed));

        var metadata = Assert.Single(results, result => result.Schema?.Id == "skycd.html.metadata");
        Assert.Equal("skycd.html.metadata", metadata.Schema!.Id);

        var preview = Assert.Single(results, result => result.Schema?.Id == "skycd.html.preview");
        var artifact = Assert.Single(preview.Preview!.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.True(artifact.Data.Length > 8);
    }

    [Fact]
    public async Task Host_TextlessDocument_HasMetadataButNoPreviewArtifacts()
    {
        var source = HtmlFixtures.Text("<html><head><script>var x = 1;</script></head><body></body></html>");

        var results = await CreateManager().ProcessAsync(
            new MediaItem(".html", "text/html", source.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(source)));

        var metadata = Assert.Single(results, result => result.Schema?.Id == "skycd.html.metadata");
        Assert.False(metadata.Failed);

        var preview = Assert.Single(results, result => result.Schema?.Id == "skycd.html.preview");
        Assert.False(preview.Failed);
        Assert.Empty(preview.Preview!.Artifacts);
    }

    [Fact]
    public void AssemblyDiscovery_FindsBothCapabilities_FromMetadataOnly()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(HtmlMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("HTML Document Metadata", plugin.Name);
        Assert.Equal(2, plugin.Capabilities.Count);
        Assert.Single(plugin.Capabilities, capability => capability is IMediaMetadataCapability);
        Assert.Single(plugin.Capabilities, capability => capability is IMediaPreviewCapability);
    }

    [Fact]
    public void PluginProject_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = FindPluginProjectDirectory();

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    private static MediaExtensionManager CreateManager(MediaExecutionLimits? limits = null) =>
        new([
            new MediaProvider(PluginId, "1.0.0", new HtmlMetadataPlugin()),
            new MediaProvider(PluginId, "1.0.0", new HtmlPreviewPlugin())
        ], limits);

    private static string FindPluginProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Metadata.Html");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the HTML metadata plugin project directory.");
    }
}
