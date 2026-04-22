using SkyCD.Plugin.Abstractions.Capabilities.Modal;
using SkyCD.Plugin.Abstractions.Lifecycle;
using SkyCD.Plugin.Host.Modal;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Plugin.Host.Tests;

public class ModalExtensionServiceTests
{
    [Fact]
    public async Task OpenAsync_ReturnsTypedPayload_WhenModalSucceeds()
    {
        var pluginCatalog = CreateCatalog(new EchoModalPlugin());
        var service = new ModalExtensionService(pluginCatalog);

        var result = await service.OpenAsync(
            new ModalOpenRequest
            {
                ModalId = "sample.modal.echo",
                Input = new ModalPayload("sample.modal.echo.input", new { Name = "SkyCD" }),
                GrantedPermissions = ["catalog.read"]
            },
            TimeSpan.FromSeconds(1));

        Assert.True(result.Success);
        Assert.NotNull(result.Output);
        Assert.Equal("sample.modal.echo.output", result.Output.TypeId);
    }

    [Fact]
    public async Task OpenAsync_ReturnsError_WhenInputTypeMismatches()
    {
        var pluginCatalog = CreateCatalog(new EchoModalPlugin());
        var service = new ModalExtensionService(pluginCatalog);

        var result = await service.OpenAsync(
            new ModalOpenRequest
            {
                ModalId = "sample.modal.echo",
                Input = new ModalPayload("wrong.type", null),
                GrantedPermissions = ["catalog.read"]
            },
            TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
        Assert.Contains("Input payload type mismatch", result.Error);
    }

    [Fact]
    public async Task OpenAsync_ReturnsError_WhenPermissionMissing()
    {
        var pluginCatalog = CreateCatalog(new EchoModalPlugin());
        var service = new ModalExtensionService(pluginCatalog);

        var result = await service.OpenAsync(
            new ModalOpenRequest
            {
                ModalId = "sample.modal.echo",
                Input = new ModalPayload("sample.modal.echo.input", null)
            },
            TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
        Assert.Contains("Missing required permissions", result.Error);
    }

    [Fact]
    public async Task OpenAsync_ReturnsCanceledResult_WhenTimeoutExpires()
    {
        var pluginCatalog = CreateCatalog(new SlowModalPlugin());
        var service = new ModalExtensionService(pluginCatalog);

        var result = await service.OpenAsync(
            new ModalOpenRequest
            {
                ModalId = "sample.modal.slow"
            },
            TimeSpan.FromMilliseconds(50));

        Assert.False(result.Success);
        Assert.True(result.Canceled);
        Assert.Contains("timed out", result.Error);
    }

    [Fact]
    public async Task OpenAsync_RejectsReentrantOpen_WhenModalDoesNotAllowIt()
    {
        var pluginCatalog = CreateCatalog(new NonReentrantDelayModalPlugin());
        var service = new ModalExtensionService(pluginCatalog);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var firstOpen = service.OpenAsync(
            new ModalOpenRequest
            {
                ModalId = "sample.modal.locked"
            },
            TimeSpan.FromSeconds(1),
            cts.Token);

        await Task.Delay(30, cts.Token);

        var second = await service.OpenAsync(
            new ModalOpenRequest
            {
                ModalId = "sample.modal.locked"
            },
            TimeSpan.FromSeconds(1),
            cts.Token);

        Assert.False(second.Success);
        Assert.Contains("already active", second.Error);
        await firstOpen;
    }

    [Fact]
    public void GetModalRegistrations_ProjectsModalMetadata()
    {
        var pluginCatalog = CreateCatalog(new EchoModalPlugin());
        var service = new ModalExtensionService(pluginCatalog);

        var registrations = service.GetModalRegistrations();
        var modal = Assert.Single(registrations);

        Assert.Equal("tests.modal.echo", modal.PluginId);
        Assert.Equal("sample.modal.echo", modal.ModalId);
        Assert.Equal("sample.modal.echo.input", modal.InputTypeId);
        Assert.Equal("sample.modal.echo.output", modal.OutputTypeId);
        Assert.True(modal.IsBlocking);
    }

    private static PluginCatalog CreateCatalog(params IModalPluginCapability[] capabilities)
    {
        var catalog = new PluginCatalog();
        catalog.SetPlugins(capabilities.Select(capability => new DiscoveredPlugin
        {
            Plugin = (IPlugin)capability,
            Capabilities = [capability]
        }));
        return catalog;
    }

    private sealed class EchoModalPlugin : IPlugin, IModalPluginCapability
    {
        public IReadOnlyCollection<ModalDescriptor> GetModals()
        {
            return
            [
                new ModalDescriptor(
                    "sample.modal.echo",
                    "Echo",
                    600,
                    320,
                    ["catalog.read"],
                    new ModalPayloadContract("sample.modal.echo.input", true),
                    new ModalPayloadContract("sample.modal.echo.output", false),
                    true,
                    false)
            ];
        }

        public Task<ModalOpenResult> OpenModalAsync(ModalOpenRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ModalOpenResult
            {
                Success = true,
                Output = new ModalPayload("sample.modal.echo.output", request.Input?.Value)
            });
        }

        public PluginDescriptor Descriptor =>
            new("tests.modal.echo", "Echo Modal", new Version(1, 0, 0), new Version(3, 0, 0));

        public ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask OnInitializeAsync(PluginLifecycleContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask OnActivateAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SlowModalPlugin : IPlugin, IModalPluginCapability
    {
        public IReadOnlyCollection<ModalDescriptor> GetModals()
        {
            return
            [
                new ModalDescriptor("sample.modal.slow", "Slow", 400, 260)
            ];
        }

        public async Task<ModalOpenResult> OpenModalAsync(ModalOpenRequest request,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            return new ModalOpenResult { Success = true };
        }

        public PluginDescriptor Descriptor =>
            new("tests.modal.slow", "Slow Modal", new Version(1, 0, 0), new Version(3, 0, 0));

        public ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask OnInitializeAsync(PluginLifecycleContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask OnActivateAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NonReentrantDelayModalPlugin : IPlugin, IModalPluginCapability
    {
        public IReadOnlyCollection<ModalDescriptor> GetModals()
        {
            return
            [
                new ModalDescriptor("sample.modal.locked", "Locked", 420, 300, AllowReentry: false)
            ];
        }

        public async Task<ModalOpenResult> OpenModalAsync(ModalOpenRequest request,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);
            return new ModalOpenResult { Success = true };
        }

        public PluginDescriptor Descriptor =>
            new("tests.modal.locked", "Locked Modal", new Version(1, 0, 0), new Version(3, 0, 0));

        public ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask OnInitializeAsync(PluginLifecycleContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask OnActivateAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}