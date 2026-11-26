using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Ui;

/// <summary>Host-provided clipboard access for plugin views.</summary>
public interface IClipboardApi
{
    /// <summary>Places text on the system clipboard.</summary>
    Task SetTextAsync(string text);
}

/// <summary>Optional capability hook the host calls with its clipboard API.</summary>
public interface IClipboardConsumer : IPluginCapability
{
    /// <summary>Receives the host clipboard API for the host process lifetime.</summary>
    void InitializeClipboard(IClipboardApi clipboard);
}
