using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Modal;

namespace SkyCD.Plugin.Sample.Modal;

public sealed class SampleModalPlugin : IModalPluginCapability
{
    public ModalDescriptor Modal =>
        new ModalDescriptor(
            "sample.modal.confirm-export",
            "Confirm Export",
            Width: 480,
            Height: 260,
            RequiredPermissions: ["catalog.read", "catalog.export"],
            InputContract: new ModalPayloadContract("sample.modal.confirm-export.input", IsRequired: true),
            OutputContract: new ModalPayloadContract("sample.modal.confirm-export.output", IsRequired: true),
            IsBlocking: true,
            AllowReentry: false);

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
