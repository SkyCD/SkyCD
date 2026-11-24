namespace SkyCD.Plugin.Runtime.Discovery;

/// <summary>
/// Function family used to choose a plugin's fallback icon when it declares none, so plugins of different
/// functions never share the same generic icon.
/// </summary>
public enum PluginIconKind
{
    FileFormat,
    Metadata,
    Preview,
    Accounts,
    Ai,
    Feature
}
