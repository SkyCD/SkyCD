using SkyCD.Plugin.Abstractions.Capabilities.Modal;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Sample.Modal;

public sealed class ModalPlugin : IPlugin, IModalPluginCapability
{
    public PluginDescriptor Descriptor => new(
        "skycd.plugin.sample.modal",
        "Sample Modal Plugin",
        new Version(1, 0, 0),
        new Version(3, 0, 0),
        "Example plugin that contributes a typed modal contract.");

    public ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask OnInitializeAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask OnActivateAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IReadOnlyCollection<ModalDescriptor> GetModals() =>
    [
        new ModalDescriptor(
            "sample.modal.confirm-export",
            "Confirm Export",
            Width: 480,
            Height: 260,
            RequiredPermissions: ["catalog.read", "catalog.export"],
            InputContract: new ModalPayloadContract("sample.modal.confirm-export.input", IsRequired: true),
            OutputContract: new ModalPayloadContract("sample.modal.confirm-export.output", IsRequired: true),
            IsBlocking: true,
            AllowReentry: false)
    ];

    public Task<ModalOpenResult> OpenModalAsync(ModalOpenRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.ModalId.Equals("sample.modal.confirm-export", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new ModalOpenResult
            {
                Success = false,
                Error = $"Unsupported modal '{request.ModalId}'."
            });
        }

        return Task.FromResult(new ModalOpenResult
        {
            Success = true,
            Output = new ModalPayload(
                "sample.modal.confirm-export.output",
                new Dictionary<string, object?>
                {
                    ["confirmed"] = true,
                    ["timestampUtc"] = DateTime.UtcNow
                })
        });
    }
}
