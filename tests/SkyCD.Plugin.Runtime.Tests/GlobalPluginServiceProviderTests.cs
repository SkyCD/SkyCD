using Microsoft.Extensions.DependencyInjection;
using PluginServiceProvider = SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class ServiceProviderTests : IDisposable
{
    public void Dispose()
    {
        PluginServiceProvider.Instance.Import(new ServiceCollection());
    }

    [Fact]
    public void Instance_ImplementsIServiceProviderAndResolvesFromCurrentProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SampleService>();

        using var provider = services.BuildServiceProvider();
        PluginServiceProvider.Instance.Import(provider);

        var resolved = PluginServiceProvider.Instance.GetService(typeof(SampleService));

        Assert.NotNull(resolved);
        Assert.IsType<SampleService>(resolved);
    }

    [Fact]
    public void Release_RemovesCurrentProviderServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SampleService>();

        using var provider = services.BuildServiceProvider();
        PluginServiceProvider.Instance.Import(provider);
        PluginServiceProvider.Instance.Import(new ServiceCollection());

        var resolved = PluginServiceProvider.Instance.GetService(typeof(SampleService));

        Assert.Null(resolved);
    }

    [Fact]
    public void Import_FromServiceCollection_ResolvesServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SampleService>();

        PluginServiceProvider.Instance.Import(services);

        var resolved = PluginServiceProvider.Instance.GetService(typeof(SampleService));

        Assert.NotNull(resolved);
        Assert.IsType<SampleService>(resolved);
    }

    [Fact]
    public void Import_FromDescriptors_ReplacesCurrentServices()
    {
        var previous = new ServiceCollection();
        previous.AddSingleton<SampleService>();
        PluginServiceProvider.Instance.Import(previous);

        var next = new ServiceCollection();
        next.AddSingleton<AnotherSampleService>();
        PluginServiceProvider.Instance.Import(next);

        var oldService = PluginServiceProvider.Instance.GetService(typeof(SampleService));
        var newService = PluginServiceProvider.Instance.GetService(typeof(AnotherSampleService));

        Assert.Null(oldService);
        Assert.NotNull(newService);
        Assert.IsType<AnotherSampleService>(newService);
    }

    private sealed class SampleService;
    private sealed class AnotherSampleService;
}
