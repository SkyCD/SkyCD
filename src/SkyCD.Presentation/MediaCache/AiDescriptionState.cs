namespace SkyCD.Presentation.MediaCache;

/// <summary>How a stored AI description relates to the current entry and generation state.</summary>
public enum AiDescriptionState
{
    /// <summary>Nothing was stored yet, or a previous session was cleared.</summary>
    Unavailable = 0,

    /// <summary>The stored description still matches the current source and context.</summary>
    Current = 1,

    /// <summary>A description exists but the source or provider changed since it was generated.</summary>
    Stale = 2,

    /// <summary>The latest attempt failed and no accepted description is available.</summary>
    Failed = 3
}
