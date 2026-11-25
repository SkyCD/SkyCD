using SkyCD.SDK.Capabilities;

namespace SkyCD.SDK.Localization;

/// <summary>Assembly-discovered plugin translations, scoped by the host to the plugin ID.</summary>
public interface ILocalizationPluginCapability : IPluginCapability, ITranslationProvider;
