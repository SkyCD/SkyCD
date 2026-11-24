using System;

namespace SkyCD.App.Icons;

/// <summary>
/// A Twemoji asset the solid-fill pipeline cannot paint flat, because the flag and toolbar artwork are
/// expected to consist only of solid-filled shapes (issue #1022).
/// </summary>
internal sealed class UnsupportedTwemojiArtworkException(string iconName, string reason)
    : InvalidOperationException($"Twemoji icon '{iconName}' is not solid-filled artwork: {reason}.");
