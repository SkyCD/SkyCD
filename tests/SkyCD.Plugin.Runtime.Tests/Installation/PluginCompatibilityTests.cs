using System;
using System.IO;
using System.Reflection;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Installation;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

/// <summary>Covers the single host-version check and the single attribute reader (issue #1228).</summary>
public sealed class PluginCompatibilityTests
{
    [Fact]
    public void Evaluator_AcceptsHostInsideStringBounds()
    {
        Assert.True(PluginCompatibilityEvaluator.IsCompatibleWithBounds(new Version(3, 5, 0), "3.0.0", "4.0.0"));
        Assert.False(PluginCompatibilityEvaluator.IsCompatibleWithBounds(new Version(2, 9, 9), "3.0.0", "4.0.0"));
        Assert.False(PluginCompatibilityEvaluator.IsCompatibleWithBounds(new Version(4, 0, 1), "3.0.0", "4.0.0"));
    }

    [Fact]
    public void Evaluator_TreatsUnparsableBoundsAsAbsent()
    {
        Assert.True(PluginCompatibilityEvaluator.IsCompatibleWithBounds(new Version(3, 5, 0), "not-a-version", null));
        Assert.True(PluginCompatibilityEvaluator.IsCompatibleWithBounds(new Version(9, 9, 9), null, "not-a-version"));
        Assert.True(PluginCompatibilityEvaluator.IsCompatibleWithBounds(new Version(3, 0, 0), null, null));
    }

    [Fact]
    public void Evaluator_FallsBackToDefaultMinWhenMinIsAbsent()
    {
        Assert.False(PluginCompatibilityEvaluator.IsCompatibleWithBounds(new Version(2, 9, 9), null, null));
        Assert.Equal(new Version(3, 0, 0), PluginCompatibilityEvaluator.DefaultMinHostVersion);
        Assert.Null(PluginCompatibilityEvaluator.TryParseBound("  "));
        Assert.Null(PluginCompatibilityEvaluator.TryParseBound("bogus"));
        Assert.Equal(new Version(3, 0, 0), PluginCompatibilityEvaluator.TryParseBound(" 3.0.0 "));
    }

    [Fact]
    public void Snapshot_DelegatesToEvaluator()
    {
        var snapshot = new PluginAssemblyIdentitySnapshot
        {
            PluginId = "tests.snapshot",
            AssemblyName = "Tests.Snapshot",
            AssemblyVersion = "1.0.0",
            EntryAssemblyPath = "Tests.Snapshot.dll",
            MinHostVersion = "3.0.0",
            MaxHostVersion = "4.0.0"
        };

        Assert.True(snapshot.IsCompatibleWith(new Version(3, 5, 0)));
        Assert.False(snapshot.IsCompatibleWith(new Version(4, 0, 1)));
        Assert.Equal(new Version(3, 0, 0), snapshot.EffectiveMinHostVersion);
        Assert.Equal(new Version(4, 0, 0), snapshot.EffectiveMaxHostVersion);
    }

    [Fact]
    public void Snapshot_FallsBackToDefaultMinForGarbage()
    {
        var snapshot = new PluginAssemblyIdentitySnapshot
        {
            PluginId = "tests.snapshot",
            AssemblyName = "Tests.Snapshot",
            AssemblyVersion = "1.0.0",
            EntryAssemblyPath = "Tests.Snapshot.dll",
            MinHostVersion = "bogus",
            MaxHostVersion = "bogus"
        };

        Assert.True(snapshot.IsCompatibleWith(new Version(3, 5, 0)));
        Assert.Equal(new Version(3, 0, 0), snapshot.EffectiveMinHostVersion);
        Assert.Null(snapshot.EffectiveMaxHostVersion);
    }

    [Fact]
    public void Reader_ReadsSliceIdentityWithoutLoading()
    {
        var attributes = PluginAssemblyAttributes.TryRead(SlicePath("Slice.dll"));

        Assert.NotNull(attributes);
        Assert.Equal("SkyCD.Plugin.SlipSlice", attributes.AssemblyName);
        Assert.Equal(new Version(2, 5, 0, 0), attributes.AssemblyVersion);
        Assert.Equal("skycd.plugin.slip-slice", attributes.PluginId);
        Assert.Equal("3.0.0", attributes.MinHostVersion);
        Assert.Equal("4.0.0", attributes.MaxHostVersion);
    }

    [Fact]
    public void Reader_ReadsMissingMaxAsAbsent()
    {
        var attributes = PluginAssemblyAttributes.TryRead(SlicePath("ForeignSlice.dll"));

        Assert.NotNull(attributes);
        Assert.Equal("skycd.plugin.foreign-slice", attributes.PluginId);
        Assert.Equal("3.0.0", attributes.MinHostVersion);
        Assert.Null(attributes.MaxHostVersion);
    }

    [Fact]
    public void Reader_ReturnsNullPluginIdForNonPluginAssembly()
    {
        var sdkPath = typeof(SkyCD.SDK.Lifecycle.PluginIdAttribute).Assembly.Location;
        var attributes = PluginAssemblyAttributes.TryRead(sdkPath);

        Assert.NotNull(attributes);
        Assert.Null(attributes.PluginId);
        Assert.True(PluginAssemblyAttributes.IsSdkAssemblyName(attributes.AssemblyName));
        Assert.False(PluginAssemblyAttributes.IsSdkAssemblyName("SkyCD.Plugin.Something"));
        Assert.False(PluginAssemblyAttributes.IsSdkAssemblyName(null));
    }

    private static string SlicePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, fileName);
}
