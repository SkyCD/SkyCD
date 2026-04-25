using System.Reflection;
using SkyCD.Plugin.Runtime.Factories;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class DiscoveredPluginFactoryTests
{
    [Fact]
    public void BuildFromAssembly_ReturnsDiscoveredPlugin_WhenAssemblyIsCompatible()
    {
        var factory = new DiscoveredPluginFactory();

        var plugin = factory.BuildFromAssembly(Assembly.GetExecutingAssembly());

        Assert.Equal("tests.runtime.assembly-plugin", plugin.Id);
        Assert.NotEmpty(plugin.Capabilities);
    }

    [Fact]
    public void BuildFromAssembly_DoesNotValidateHostCompatibility()
    {
        var factory = new DiscoveredPluginFactory();

        var plugin = factory.BuildFromAssembly(Assembly.GetExecutingAssembly());
        Assert.Equal(new Version(3, 0, 0), plugin.MinHostVersion);
        Assert.Equal(new Version(3, 0, 0), plugin.MaxHostVersion);
    }
}
