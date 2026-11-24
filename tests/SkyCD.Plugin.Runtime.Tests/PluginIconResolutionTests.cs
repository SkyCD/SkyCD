using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.Accounts;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

/// <summary>
/// Issue #1036: a plugin declares its icon through assembly metadata, and every plugin without one gets a
/// well-defined fallback chosen by capability kind.
/// </summary>
public sealed class PluginIconResolutionTests
{
    [Fact]
    public void BuildFromAssembly_UsesTheDeclaredPluginIcon()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(Assembly.GetExecutingAssembly());

        Assert.Equal("Lucide:Archive", plugin.Icon);
        Assert.Equal(PluginIconKind.FileFormat, plugin.IconKind);
    }

    [Theory]
    [InlineData("SkyCD.Plugin.FileFormat.Json", PluginIconKind.FileFormat)]
    [InlineData("SkyCD.Plugin.Metadata.Audio", PluginIconKind.Metadata)]
    [InlineData("SkyCD.Plugin.Volume.Serial", PluginIconKind.Metadata)]
    [InlineData("SkyCD.Plugin.Preview.Image", PluginIconKind.Preview)]
    [InlineData("SkyCD.Plugin.Accounts.Core", PluginIconKind.Accounts)]
    [InlineData("SkyCD.Plugin.Ai.Description", PluginIconKind.Ai)]
    [InlineData("SkyCD.Plugin.Feature.Split", PluginIconKind.Feature)]
    public void AssemblyName_ResolvesFirstPartyKind(string assemblyName, PluginIconKind expected)
    {
        Assert.True(PluginIconResolver.TryResolveKindFromAssemblyName(assemblyName, out var kind));
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData("SkyCD.Plugin.Sample.Config")]
    [InlineData("SkyCD.Plugin.Runtime.Tests")]
    [InlineData("Acme.SkyCD.Plugin.Accounts.Core")]
    [InlineData("MyPlugin")]
    public void AssemblyName_RejectsNamesWithoutAPluginKind(string assemblyName)
    {
        Assert.False(PluginIconResolver.TryResolveKindFromAssemblyName(assemblyName, out _));
    }

    [Fact]
    public void Capabilities_FallBackMetadataBeforePreview()
    {
        IPluginCapability[] capabilities = [new PreviewCapability(0), new MetadataCapability(0)];

        Assert.Equal(PluginIconKind.Metadata, PluginIconResolver.ResolveKindFromCapabilities(capabilities));
    }

    [Fact]
    public void Capabilities_ResolveEachKind()
    {
        Assert.Equal(PluginIconKind.Metadata,
            PluginIconResolver.ResolveKindFromCapabilities([new MetadataCapability(0)]));
        Assert.Equal(PluginIconKind.Preview,
            PluginIconResolver.ResolveKindFromCapabilities([new PreviewCapability(0)]));
        Assert.Equal(PluginIconKind.Accounts,
            PluginIconResolver.ResolveKindFromCapabilities([new AccountConnectorCapability(0)]));
        Assert.Equal(PluginIconKind.Ai,
            PluginIconResolver.ResolveKindFromCapabilities([new GeneratedDescriptionCapability(0)]));
    }

    [Fact]
    public void Capabilities_WithoutAKnownSurfaceFallBackToFeature()
    {
        Assert.Equal(PluginIconKind.Feature,
            PluginIconResolver.ResolveKindFromCapabilities([new PlainCapability(0)]));
        Assert.Equal(PluginIconKind.Feature,
            PluginIconResolver.ResolveKindFromCapabilities([]));
    }

    // The fakes take a constructor argument so the capability scan, which only instantiates parameterless
    // types, cannot pick them up when the whole test assembly is treated as a plugin.
    private sealed class PlainCapability(int _) : IPluginCapability;

    private abstract class MediaCapability : IPluginCapability
    {
        public MediaDescriptor Descriptor { get; } = new([".bin"], ["application/octet-stream"],
            new MediaMetadataSchema("test", 1, []));
    }

    private sealed class MetadataCapability(int _) : MediaCapability, IMediaMetadataCapability
    {
        public Task<MediaExtractionResult> ExtractAsync(MediaItem item, IMediaSource source,
            IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new MediaExtractionResult([]));
    }

    private sealed class PreviewCapability(int _) : MediaCapability, IMediaPreviewCapability
    {
        public Task<MediaPreviewResult> GeneratePreviewAsync(MediaItem item, IMediaSource source,
            IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new MediaPreviewResult([]));
    }

    private sealed class AccountConnectorCapability(int _) : IAccountConnector
    {
        public Task<AccountConnectionResult> ConnectAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class GeneratedDescriptionCapability(int _) : IGeneratedDescriptionCapability
    {
        public MediaMetadataSchema Schema { get; } = new("test", 1, []);

        public Task<GeneratedDescription> GenerateAsync(DescriptionContext context,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
