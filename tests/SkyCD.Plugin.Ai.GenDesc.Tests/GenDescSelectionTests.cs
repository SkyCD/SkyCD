using System.Linq;
using SkyCD.Plugin.Host.Media;
using Xunit;

namespace SkyCD.Plugin.Ai.GenDesc.Tests;

/// <summary>
/// Confirms the real plugin satisfies the host's implementation surface end to end: it appears as a named,
/// selectable choice in <see cref="DescriptionExtensionManager"/> alongside other implementations, which is the
/// acceptance criterion "selectable implementation in the picker" without depending on UI.
/// </summary>
public class GenDescSelectionTests
{
    [Fact]
    public void GenDescIsASelectableImplementation()
    {
        var plugin = new GenDescPlugin("unused-path", StubOnlineMetadataService.Wikipedia());
        var manager = new DescriptionExtensionManager([plugin]);

        var choice = Assert.Single(manager.Implementations);
        Assert.Equal("gendesc", choice.Id);
        Assert.Equal("GenDesc templates", choice.Name);

        manager.SelectImplementation("gendesc");
        Assert.Same(plugin, manager.Capability);
        Assert.True(manager.IsAvailable);
    }
}
