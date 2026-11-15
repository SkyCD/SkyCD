namespace SkyCD.Presentation.MediaCache;

/// <summary>Where a catalog property value came from.</summary>
public enum MediaMetadataOrigin
{
    /// <summary>Captured by a plugin and stored in the media cache.</summary>
    Captured = 0,

    /// <summary>Entered or edited by the user; always wins over captured values.</summary>
    UserEdited = 1
}
