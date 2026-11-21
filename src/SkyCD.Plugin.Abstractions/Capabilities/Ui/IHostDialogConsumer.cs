namespace SkyCD.Plugin.Abstractions.Capabilities.Ui;

/// <summary>
/// Optional capability hook the host calls to give a plugin a dialog API before its services are published,
/// so the plugin shows windows through the host instead of constructing them against a live application lifetime.
/// </summary>
public interface IHostDialogConsumer : IPluginCapability
{
    /// <summary>Receives the host dialog API for the host process lifetime.</summary>
    void InitializeDialogApi(IHostDialogApi dialogApi);
}
