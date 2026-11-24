using SkyCD.Plugin.Abstractions.Capabilities;

namespace SkyCD.Plugin.Abstractions.Localization;

/// <summary>Assembly-discovered plugin translations, scoped by the host to the plugin ID.</summary>
public interface ILocalizationPluginCapability : IPluginCapability, ITranslationProvider;
