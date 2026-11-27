using System;
using System.Linq;
using SkyCD.SDK.Capabilities;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class SdkSurfaceTests
{
    [Fact]
    public void SdkAssemblyReferencesOnlyFrameworkAssemblies()
    {
        var references = typeof(IPluginCapability).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                && !name.Equals("netstandard", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("Microsoft.CSharp", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("Microsoft.VisualBasic", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(references);
    }
}
