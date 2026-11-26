using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Metadata.Notebook.Models;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.SDK.Capabilities.Media;
using Xunit;

namespace SkyCD.Plugin.Metadata.Notebook.Tests;

/// <summary>
/// End-to-end coverage for the notebook plugin inside the host pipeline: provider selection, host
/// resource budgets, failure isolation, and assembly discovery without a manifest.
/// </summary>
public sealed class NotebookPluginIntegrationTests
{
    private const string PluginId = "SkyCD.Plugin.Notebook";

    [Fact]
    public async Task Host_ExtractsMetadataForANotebook()
    {
        var source = NotebookFixtures.Bytes(NotebookFixtures.FullNotebook);
        var results = await CreateManager().ProcessAsync(
            new MediaItem(".ipynb", "application/x-ipynb+json", source.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(source)));

        var metadata = Assert.Single(results, result => result.Schema?.Id == NotebookSchema.MetadataSchemaId);
        Assert.False(metadata.Failed);
        Assert.Equal(3, metadata.Metadata!.Values.Single(value => value.Key == NotebookSchema.CellCount).Integer);
        Assert.Equal("python", metadata.Metadata.Values.Single(value => value.Key == NotebookSchema.Language).Text);
    }

    [Fact]
    public void Host_ClaimsOnlyIpynbSources()
    {
        var manager = CreateManager();

        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".ipynb", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".IPYNB", "application/x-ipynb+json", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".qmd", null, 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".Rmd", null, 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".nb.html", null, 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".deepnote", null, 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".zpln", null, 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".md", "text/markdown", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".py", null, 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".json", "application/json", 10)));
    }

    [Fact]
    public async Task Host_ReportsInvalidInputAsMetadataInsteadOfAFailure()
    {
        var source = NotebookFixtures.Bytes(NotebookFixtures.CorruptNotebook);
        var results = await CreateManager().ProcessAsync(
            new MediaItem(".ipynb", "application/x-ipynb+json", source.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(source)));

        var metadata = Assert.Single(results, result => result.Schema?.Id == NotebookSchema.MetadataSchemaId);
        Assert.False(metadata.Failed);
        Assert.False(metadata.Metadata!.Values.Single(value => value.Key == NotebookSchema.Valid).Boolean);
        Assert.Equal("invalid-json",
            metadata.Metadata.Values.Single(value => value.Key == NotebookSchema.ErrorCode).Text);
    }

    [Fact]
    public async Task HostSourceBudget_RejectsANotebookLargerThanTheBudget()
    {
        var source = NotebookFixtures.Bytes(NotebookFixtures.FullNotebook);
        var manager = CreateManager(new MediaExecutionLimits(SourceBytes: 16));

        var results = await manager.ProcessAsync(
            new MediaItem(".ipynb", "application/x-ipynb+json", source.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(source)));

        Assert.All(results, result => Assert.True(result.Failed));
    }

    [Fact]
    public void AssemblyDiscovery_FindsTheCapability_FromMetadataOnly()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(NotebookMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("Jupyter Notebook Metadata", plugin.Name);
        Assert.Single(plugin.Capabilities);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaMetadataCapability);
    }

    [Fact]
    public void PluginProject_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = FindPluginProjectDirectory();

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void OfficeImoPackages_ShareOneVersion()
    {
        var root = FindRepositoryRoot();
        var versions = Directory.EnumerateFiles(Path.Combine(root, "Plugins"), "*.csproj", SearchOption.AllDirectories)
            .SelectMany(path => Regex.Matches(
                File.ReadAllText(path),
                "PackageReference Include=\"(OfficeIMO\\.[^\"]+)\" Version=\"([^\"]+)\""))
            .Select(match => match.Groups[2].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(versions);
        Assert.Single(versions);
    }

    private static MediaExtensionManager CreateManager(MediaExecutionLimits? limits = null) =>
        new([
            new MediaProvider(PluginId, "1.0.0", new NotebookMetadataPlugin())
        ], limits);

    private static string FindPluginProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Metadata.Notebook");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the notebook metadata plugin project directory.");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SkyCD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
