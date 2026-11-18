namespace SkyCD.Plugin.Host.Media;

/// <summary>Which matching providers a host pass may execute.</summary>
public enum MediaProviderSelection
{
    /// <summary>Only providers that run as part of normal preview and metadata extraction.</summary>
    Standard,

    /// <summary>Only opt-in providers, such as whole-file hashing.</summary>
    OptInOnly,

    /// <summary>Standard metadata providers without preview generation.</summary>
    StandardMetadata,

    /// <summary>Opt-in metadata providers without preview generation.</summary>
    OptInMetadata,

    /// <summary>Every matching provider.</summary>
    All
}
