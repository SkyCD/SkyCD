using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using SkyCD.SDK.Capabilities;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;

namespace SkyCD.Plugin.Ai.Provider.Tests;

public sealed class DescriptionExtensionManagerTests
{
    private static readonly MediaMetadataSchema Schema = new("test", 1, Array.Empty<MediaMetadataField>());

    [Fact]
    public void Implementations_ListsIdentifiedCapabilities()
    {
        var manager = new DescriptionExtensionManager([new Stub("acp", "ACP"), new Stub("provider", "Provider")]);

        Assert.Equal(new[] { "acp", "provider" },
            manager.Implementations.Select(static choice => choice.Id));
    }

    [Fact]
    public void SelectImplementation_ChoosesRequestedCapability()
    {
        var acp = new Stub("acp", "ACP");
        var provider = new Stub("provider", "Provider");
        var manager = new DescriptionExtensionManager([acp, provider]);

        manager.SelectImplementation("provider");

        Assert.Same(provider, manager.Capability);
        Assert.Equal("provider", manager.ActiveImplementationId);
        Assert.Equal("provider", manager.SelectedImplementationId);
    }

    [Fact]
    public void SelectImplementation_IsCaseInsensitive()
    {
        var acp = new Stub("acp", "ACP");
        var provider = new Stub("provider", "Provider");
        var manager = new DescriptionExtensionManager([acp, provider]);

        manager.SelectImplementation("PROVIDER");

        Assert.Same(provider, manager.Capability);
    }

    [Fact]
    public void SelectImplementation_WhenUnknown_FallsBackToFirstAvailable()
    {
        var acp = new Stub("acp", "ACP");
        var provider = new Stub("provider", "Provider");
        var manager = new DescriptionExtensionManager([acp, provider]);

        manager.SelectImplementation("removed");

        Assert.Same(acp, manager.Capability);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void SelectImplementation_WhenEmpty_SelectsFirstAvailable(string? id)
    {
        var acp = new Stub("acp", "ACP");
        var provider = new Stub("provider", "Provider");
        var manager = new DescriptionExtensionManager([acp, provider]);

        manager.SelectImplementation("provider");
        manager.SelectImplementation(id);

        Assert.Same(acp, manager.Capability);
        Assert.Null(manager.SelectedImplementationId);
    }

    [Fact]
    public void Constructor_SelectsFirstCapability()
    {
        var acp = new Stub("acp", "ACP");
        var manager = new DescriptionExtensionManager([acp, new Stub("provider", "Provider")]);

        Assert.Same(acp, manager.Capability);
        Assert.True(manager.IsAvailable);
    }

    [Fact]
    public void WhenNoCapabilities_ManagerIsUnavailable()
    {
        var manager = new DescriptionExtensionManager([]);

        Assert.False(manager.IsAvailable);
        Assert.Null(manager.Capability);
        Assert.Null(manager.ActiveImplementationId);
    }

    private sealed class Stub(string id, string name) : IDescriptionGeneratorCapability
    {
        public string ImplementationId => id;

        public string ImplementationName => name;

        public MediaMetadataSchema Schema => DescriptionExtensionManagerTests.Schema;

        public Task<GeneratedDescription> GenerateAsync(DescriptionContext context,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
