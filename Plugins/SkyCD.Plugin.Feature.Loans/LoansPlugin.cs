using SkyCD.Plugin.Abstractions.Capabilities;

namespace SkyCD.Plugin.Feature.Loans;

/// <summary>
/// First-party catalog feature plugin that tracks item loans and borrowers. The storage, service, view
/// models and windows for the feature live in this assembly; later stages wire its capabilities into the
/// host render seams so the core no longer carries any lending code.
/// </summary>
public sealed class LoansPlugin : IPluginCapability
{
}
