using System;

namespace SkyCD.Plugin.Metadata.GameDev.Exceptions;

/// <summary>Raised when a container exceeds an entry, decompression or scan bound.</summary>
public sealed class GameDevLimitExceededException : Exception
{
    public GameDevLimitExceededException()
        : base("The game development file exceeds the configured scan bounds.")
    {
    }
}
