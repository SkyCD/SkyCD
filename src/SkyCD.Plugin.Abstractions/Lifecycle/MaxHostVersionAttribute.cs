namespace SkyCD.Plugin.Abstractions.Lifecycle;

/// <summary>
/// Defines an optional maximum supported host version at assembly level.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MaxHostVersionAttribute(string version) : Attribute
{
    /// <summary>
    /// Gets the maximum host version value.
    /// </summary>
    public string Version { get; } = version;
}
