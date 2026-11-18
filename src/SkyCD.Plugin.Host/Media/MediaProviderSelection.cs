namespace SkyCD.Plugin.Host.Media;

/// <summary>Which matching providers a host pass may execute.</summary>
public enum MediaProviderSelection
{
    /// <summary>Only providers that run as part of normal preview and metadata extraction.</summary>
    Standard,

    /// <summary>Only providers that must be requested explicitly, such as whole-file hashing.</summary>
    OptInOnly,

    /// <summary>Every matching provider.</summary>
    All
}
