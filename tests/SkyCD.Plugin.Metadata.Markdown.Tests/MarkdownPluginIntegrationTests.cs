using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.Plugin.Metadata.Markdown.Models;

namespace SkyCD.Plugin.Metadata.Markdown.Tests;

/// <summary>
/// End-to-end coverage for the Markdown plugin inside the host pipeline: provider selection, host resource
/// budgets, failure isolation, and assembly discovery without a manifest.
/// </summary>
public sealed class MarkdownPluginIntegrationTests
{
    private const string PluginId = "SkyCD.Plugin.Markdown.Metadata";

    [Fact]
    public async Task Host_ExtractsMetadataForASingleDocument()
    {
        var source = MarkdownFixtures.WithFrontMatter(
            "title: Integration\nauthor: A. Writer", "# Heading\n\nBody paragraph.");

        var results = await CreateManager().ProcessAsync(
            new MediaItem(".md", "text/markdown", source.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(source)));

        var metadata = Assert.Single(results);
        Assert.False(metadata.Failed);
        Assert.Equal("skycd.markdown.metadata", metadata.Schema!.Id);
        Assert.Equal("Integration", metadata.Metadata!.Values.Single(value => value.Key == MarkdownSchema.Title).Text);
        Assert.Equal("Heading",
            metadata.Metadata!.Values.Single(value => value.Key == MarkdownSchema.FirstHeading).Text);
    }

    [Fact]
    public void Host_ClaimsOnlyMarkdownSources()
    {
        var manager = CreateManager();

        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".md", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".MARKDOWN", "text/markdown", 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".md", "text/x-markdown", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".mdx", null, 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".txt", "text/plain", 10)));
    }

    [Fact]
    public async Task UndecodableDocument_IsReportedAsAFailedProviderInsteadOfBreakingThePass()
    {
        var source = MarkdownFixtures.InvalidUtf8();
        var results = await CreateManager().ProcessAsync(
            new MediaItem(".md", "text/markdown", source.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(source)));

        var result = Assert.Single(results);
        Assert.True(result.Failed);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task HostSourceBudget_RejectsADocumentLargerThanTheBudget()
    {
        var source = MarkdownFixtures.Text("# Larger than the budget");
        var manager = CreateManager(new MediaExecutionLimits(SourceBytes: 16));

        var results = await manager.ProcessAsync(
            new MediaItem(".md", "text/markdown", source.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(source)));

        Assert.All(results, result => Assert.True(result.Failed));
    }

    [Fact]
    public void AssemblyDiscovery_FindsTheCapability_FromMetadataOnly()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(MarkdownMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("Markdown Document Metadata", plugin.Name);
        var capability = Assert.Single(plugin.Capabilities);
        Assert.IsAssignableFrom<IMediaMetadataCapability>(capability);
    }

    [Fact]
    public void PluginProject_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = FindPluginProjectDirectory();

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    private static MediaExtensionManager CreateManager(MediaExecutionLimits? limits = null) =>
        new([new MediaProvider(PluginId, "1.0.0", new MarkdownMetadataPlugin())], limits);

    private static string FindPluginProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Metadata.Markdown");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the Markdown metadata plugin project directory.");
    }
}
