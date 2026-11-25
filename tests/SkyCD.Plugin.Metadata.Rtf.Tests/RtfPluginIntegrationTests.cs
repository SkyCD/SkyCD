using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Formatting.Rtf;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.Plugin.Metadata.Rtf.Export;
using SkyCD.Plugin.Metadata.Rtf.Models;

namespace SkyCD.Plugin.Metadata.Rtf.Tests;

/// <summary>
/// End-to-end coverage for the RTF plugin inside the host pipeline: provider selection, host resource
/// budgets, the host's own timeout, failure isolation, and assembly discovery without a manifest.
/// </summary>
public sealed class RtfPluginIntegrationTests
{
    private const string PluginId = "SkyCD.Plugin.Rtf";

    private static readonly byte[] RenderedThumbnail = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    [Fact]
    public async Task Host_RunsBothCapabilitiesForASingleDocument()
    {
        var rtf = RtfFixtures.Document(
            title: "Integration",
            author: "A. Writer",
            createdAt: new DateTimeOffset(2021, 3, 4, 12, 34, 56, TimeSpan.Zero));

        var results = await CreateManager().ProcessAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(rtf)));

        var metadata = results.Single(result => result.Metadata is not null);
        var preview = results.Single(result => result.Preview is not null);

        Assert.False(metadata.Failed);
        Assert.False(preview.Failed);
        Assert.Equal("skycd.rtf.metadata", metadata.Schema!.Id);
        Assert.Equal("skycd.rtf.preview", preview.Schema!.Id);
        Assert.Equal("Integration", metadata.Metadata!.Values.Single(value => value.Key == RtfSchema.Title).Text);
        Assert.Equal(RenderedThumbnail, Assert.Single(preview.Preview!.Artifacts).Data.ToArray());
    }

    [Fact]
    public void Host_ClaimsOnlyRtfSources()
    {
        var manager = CreateManager();

        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".rtf", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".RTF", "application/rtf", 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".rtf", "text/rtf", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".txt", "text/plain", 10)));
    }

    [Fact]
    public async Task NonRtfDocument_IsReportedAsAFailedProviderInsteadOfBreakingThePass()
    {
        var notRtf = RtfFixtures.NotRtf();
        var results = await CreateManager().ProcessAsync(
            new MediaItem(".rtf", "application/rtf", notRtf.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(notRtf)));

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.True(result.Failed));
        Assert.All(results, result => Assert.Null(result.Metadata));
    }

    [Fact]
    public async Task HostSourceBudget_RejectsADocumentLargerThanTheBudget()
    {
        var rtf = RtfFixtures.Document();
        var manager = CreateManager(new MediaExecutionLimits(SourceBytes: 16));

        var results = await manager.ProcessAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(rtf)));

        Assert.All(results, result => Assert.True(result.Failed));
    }

    [Fact]
    public async Task SlowRenderer_IsTimedOutByTheHostWithoutHangingThePass()
    {
        var rtf = RtfFixtures.Document(title: "Slow");
        var manager = CreateManager(new MediaExecutionLimits(TimeoutMilliseconds: 150), slowRendererDelay: 10_000);

        var results = await manager.ProcessAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(rtf)));

        var preview = results.Single(result => result.Capability.Contains(nameof(RtfPreviewPlugin), StringComparison.Ordinal));
        Assert.True(preview.Failed);
        Assert.True(preview.TimedOut);

        // Metadata does not depend on the renderer, so it still succeeds in the same pass.
        var metadata = results.Single(result => result.Metadata is not null);
        Assert.False(metadata.Failed);
    }

    [Fact]
    public async Task NoRenderer_StillStoresMetadataWithNoArtifact()
    {
        var rtf = RtfFixtures.Document(title: "Metadata only");
        var manager = CreateManager(rendererAvailable: false);

        var results = await manager.ProcessAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(rtf)));

        var preview = results.Single(result => result.Capability.Contains(nameof(RtfPreviewPlugin), StringComparison.Ordinal));
        Assert.False(preview.Failed);
        Assert.Empty(preview.Preview!.Artifacts);

        var metadata = results.Single(result => result.Metadata is not null);
        Assert.False(metadata.Failed);
        Assert.Equal("Metadata only", metadata.Metadata!.Values.Single(value => value.Key == RtfSchema.Title).Text);
    }

    [Fact]
    public async Task CancelledPass_LeavesNoProviderWithACompletedResult()
    {
        var rtf = RtfFixtures.Document();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateManager().ProcessAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(rtf)),
            progress: null,
            cancelled.Token));
    }

    [Fact]
    public void AssemblyDiscovery_FindsBothCapabilities_FromMetadataOnly()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(RtfMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("Rich Text Format Document Metadata and Preview", plugin.Name);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaMetadataCapability);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaPreviewCapability);
        Assert.Equal(2, plugin.Capabilities.Count);
    }

    [Fact]
    public void PluginProject_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = FindPluginProjectDirectory();

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    private static MediaProvider[] Providers(bool rendererAvailable, int slowRendererDelay) =>
    [
        new MediaProvider(PluginId, "1.0.0", new RtfMetadataPlugin()),
        new MediaProvider(PluginId, "1.0.0", new RtfPreviewPlugin(
            RtfPluginOptions.Default,
            new StubRenderer(rendererAvailable, rendererAvailable ? RenderedThumbnail : null, slowRendererDelay)))
    ];

    private static MediaExtensionManager CreateManager(
        MediaExecutionLimits? limits = null,
        bool rendererAvailable = true,
        int slowRendererDelay = 0) =>
        new(Providers(rendererAvailable, slowRendererDelay), limits);

    private static string FindPluginProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Metadata.Rtf");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the RTF plugin project directory.");
    }

    /// <summary>Stands in for the Skia renderer, optionally slowly, so host timeouts can be exercised.</summary>
    private sealed class StubRenderer : IRtfThumbnailRenderer
    {
        private readonly byte[]? image;
        private readonly int delayMilliseconds;

        public StubRenderer(bool available, byte[]? image, int delayMilliseconds)
        {
            IsAvailable = available;
            this.image = image;
            this.delayMilliseconds = delayMilliseconds;
        }

        public bool IsAvailable { get; }

        public async Task<byte[]?> RenderAsync(
            RichTextDocument document,
            int maxDimension,
            int maxCharacters,
            CancellationToken cancellationToken)
        {
            if (delayMilliseconds > 0)
            {
                await Task.Delay(delayMilliseconds, cancellationToken);
            }

            return image;
        }
    }
}
