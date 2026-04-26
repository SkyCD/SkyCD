using SkyCD.Plugin.Abstractions.Capabilities.Modal;
using SkyCD.Plugin.Host.Modal;

namespace SkyCD.Plugin.Host.Tests;

public class ModalExtensionServiceTests
{
    [Fact]
    public async Task OpenAsync_ReturnsTypedPayload_WhenModalSucceeds()
    {
        var plugins = CreateCatalog(new EchoModalPlugin());
        var service = new ModalExtensionManager(plugins);

        var result = await service.OpenAsync(
            new ModalOpenRequest
            {
                ModalId = "sample.modal.echo",
                Input = new ModalPayload("sample.modal.echo.input", new { Name = "SkyCD" }),
                GrantedPermissions = ["catalog.read"]
            },
            timeout: TimeSpan.FromSeconds(1));

        Assert.True(result.Success);
        Assert.NotNull(result.Output);
        Assert.Equal("sample.modal.echo.output", result.Output.TypeId);
    }

    [Fact]
    public async Task OpenAsync_ReturnsError_WhenInputTypeMismatches()
    {
        var plugins = CreateCatalog(new EchoModalPlugin());
        var service = new ModalExtensionManager(plugins);

        var result = await service.OpenAsync(
            new ModalOpenRequest
            {
                ModalId = "sample.modal.echo",
                Input = new ModalPayload("wrong.type", null),
                GrantedPermissions = ["catalog.read"]
            },
            timeout: TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
        Assert.Contains("Input payload type mismatch", result.Error);
    }

    [Fact]
    public async Task OpenAsync_ReturnsError_WhenPermissionMissing()
    {
        var plugins = CreateCatalog(new EchoModalPlugin());
        var service = new ModalExtensionManager(plugins);

        var result = await service.OpenAsync(
            new ModalOpenRequest
            {
                ModalId = "sample.modal.echo",
                Input = new ModalPayload("sample.modal.echo.input", null)
            },
            timeout: TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
        Assert.Contains("Missing required permissions", result.Error);
    }

    [Fact]
    public async Task OpenAsync_ReturnsCanceledResult_WhenTimeoutExpires()
    {
        var plugins = CreateCatalog(new SlowModalPlugin());
        var service = new ModalExtensionManager(plugins);

        var result = await service.OpenAsync(
            new ModalOpenRequest
            {
                ModalId = "sample.modal.slow"
            },
            timeout: TimeSpan.FromMilliseconds(50));

        Assert.False(result.Success);
        Assert.True(result.Canceled);
        Assert.Contains("timed out", result.Error);
    }

    [Fact]
    public async Task OpenAsync_RejectsReentrantOpen_WhenModalDoesNotAllowIt()
    {
        var plugin = new NonReentrantControlledModalPlugin();
        var plugins = CreateCatalog(plugin);
        var service = new ModalExtensionManager(plugins);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var firstOpen = service.OpenAsync(
            new ModalOpenRequest
            {
                ModalId = "sample.modal.locked"
            },
            timeout: TimeSpan.FromSeconds(1),
            cancellationToken: cts.Token);

        await plugin.FirstOpenStarted.WaitAsync(cts.Token);

        var second = await service.OpenAsync(
            new ModalOpenRequest
            {
                ModalId = "sample.modal.locked"
            },
            timeout: TimeSpan.FromSeconds(1),
            cancellationToken: cts.Token);

        Assert.False(second.Success);
        Assert.Contains("already active", second.Error);
        plugin.AllowCompletion();

        var first = await firstOpen;
        Assert.True(first.Success);
    }

    [Fact]
    public void GetModalRegistrations_ProjectsModalMetadata()
    {
        var plugins = CreateCatalog(new EchoModalPlugin());
        var service = new ModalExtensionManager(plugins);

        var registrations = service.GetModalRegistrations();
        var modal = Assert.Single(registrations);

        Assert.Equal(typeof(EchoModalPlugin).Assembly.GetName().Name, modal.PluginId);
        Assert.Equal("sample.modal.echo", modal.ModalId);
        Assert.Equal("sample.modal.echo.input", modal.InputTypeId);
        Assert.Equal("sample.modal.echo.output", modal.OutputTypeId);
        Assert.True(modal.IsBlocking);
    }

    private static IReadOnlyCollection<IModalPluginCapability> CreateCatalog(params IModalPluginCapability[] capabilities)
    {
        return capabilities.ToList();
    }

    private sealed class EchoModalPlugin : IModalPluginCapability
    {
        public ModalDescriptor Modal =>
            new ModalDescriptor(
                "sample.modal.echo",
                "Echo",
                Width: 600,
                Height: 320,
                RequiredPermissions: ["catalog.read"],
                InputContract: new ModalPayloadContract("sample.modal.echo.input", IsRequired: true),
                OutputContract: new ModalPayloadContract("sample.modal.echo.output", IsRequired: false),
                IsBlocking: true,
                AllowReentry: false);

        public Task<ModalOpenResult> OpenModalAsync(ModalOpenRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ModalOpenResult
            {
                Success = true,
                Output = new ModalPayload("sample.modal.echo.output", request.Input?.Value)
            });
        }
    }

    private sealed class SlowModalPlugin : IModalPluginCapability
    {
        public ModalDescriptor Modal => new("sample.modal.slow", "Slow", 400, 260);

        public async Task<ModalOpenResult> OpenModalAsync(ModalOpenRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            return new ModalOpenResult { Success = true };
        }
    }

    private sealed class NonReentrantControlledModalPlugin : IModalPluginCapability
    {
        private readonly TaskCompletionSource _firstOpenStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _allowCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstOpenStarted => _firstOpenStarted.Task;

        public void AllowCompletion() => _allowCompletion.TrySetResult();

        public ModalDescriptor Modal => new("sample.modal.locked", "Locked", 420, 300, AllowReentry: false);

        public async Task<ModalOpenResult> OpenModalAsync(ModalOpenRequest request, CancellationToken cancellationToken = default)
        {
            _firstOpenStarted.TrySetResult();
            await _allowCompletion.Task.WaitAsync(cancellationToken);
            return new ModalOpenResult { Success = true };
        }
    }
}
