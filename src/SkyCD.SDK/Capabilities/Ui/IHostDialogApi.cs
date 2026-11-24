using System;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Ui;

/// <summary>
/// A request to show a plugin-built control inside a host-owned dialog window. <see cref="CreateContent"/>
/// returns an embeddable control (never a Window), matching the <c>PluginConfigurationPage</c> precedent, so
/// the host owns window chrome, modality and disposal and a headless host can decline to render.
/// </summary>
public sealed record HostWindowRequest(
    string Title,
    int Width,
    int Height,
    Func<object> CreateContent);

/// <summary>Host-provided surface for showing plugin UI in a host-managed window.</summary>
public interface IHostDialogApi
{
    /// <summary>Shows the plugin content modally in a host window and completes when the window closes.</summary>
    Task ShowAsync(HostWindowRequest request);
}
