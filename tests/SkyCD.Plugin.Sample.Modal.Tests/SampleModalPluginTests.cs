using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Modal;
using SkyCD.Plugin.Host.Modal;
using SkyCD.Plugin.Sample.Modal;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class SampleModalPluginTests
{
    [Fact]
    public async Task OpenAsync_ReturnsConfirmedOutput_ForSampleModal()
    {
        var service = new ModalExtensionManager([new SampleModalPlugin()]);

        var request = new ModalOpenRequest
        {
            ModalId = "sample.modal.confirm-export",
            Input = new ModalPayload("sample.modal.confirm-export.input", new Dictionary<string, object?>
            {
                ["selectedCount"] = 3
            }),
            GrantedPermissions = ["catalog.read", "catalog.export"]
        };

        var result = await service.OpenAsync(request, timeout: TimeSpan.FromSeconds(1));

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Output);
        Assert.Equal("sample.modal.confirm-export.output", result.Output!.TypeId);
        var payload = Assert.IsType<Dictionary<string, object?>>(result.Output.Value);
        Assert.Equal("True", payload["confirmed"]?.ToString());
        Assert.NotNull(payload["timestampUtc"]);
    }

    [Fact]
    public async Task OpenAsync_ReturnsError_WhenRequiredPermissionMissing()
    {
        var service = new ModalExtensionManager([new SampleModalPlugin()]);
        var request = new ModalOpenRequest
        {
            ModalId = "sample.modal.confirm-export",
            Input = new ModalPayload("sample.modal.confirm-export.input", null),
            GrantedPermissions = ["catalog.read"]
        };

        var result = await service.OpenAsync(request, timeout: TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
        Assert.Contains("Missing required permissions", result.Error);
    }
}
