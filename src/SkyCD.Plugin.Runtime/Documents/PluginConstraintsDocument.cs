namespace SkyCD.Plugin.Runtime.Documents;

public sealed class PluginConstraintsDocument
{
    public string MinHostVersion { get; set; } = "3.0.0";

    public string? MaxHostVersion { get; set; }
}
