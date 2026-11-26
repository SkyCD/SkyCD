namespace SkyCD.SDK.Capabilities.Options;

/// <summary>Describes one page a plugin contributes to the host's Options dialog.</summary>
/// <param name="PageId">Stable identifier; the persisted Options selection survives translated title changes.</param>
/// <param name="Title">Localized page title shown in the Options navigation and search.</param>
/// <param name="Order">Position among contributed pages; lower values come first.</param>
/// <param name="Group">
/// Optional localized sidebar group. Pages sharing a group render under one non-selectable header; keep the
/// members' orders contiguous so the group stays together.
/// </param>
public sealed record OptionsPageDescriptor(string PageId, string Title, int Order = 0, string? Group = null);

/// <summary>
/// Capability contract for plugins that contribute a first-class Options page instead of opening a floating
/// window. The host owns window chrome, navigation and disposal, and a headless host can decline to render.
/// </summary>
public interface IOptionsPageCapability : IPluginCapability
{
    /// <summary>Gets the descriptor of the contributed page.</summary>
    OptionsPageDescriptor OptionsPage { get; }

    /// <summary>
    /// Creates the page's embeddable control (never a Window). Called once per Options session on the UI
    /// thread; the host disposes the result when Options closes.
    /// </summary>
    object CreateOptionsPage();
}
