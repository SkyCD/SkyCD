using System;

namespace SkyCD.Plugin.Metadata.GameDev.Exceptions;

/// <summary>Raised when a container is recognized but its bounded structure cannot be followed safely.</summary>
public sealed class GameDevDecodeException : Exception
{
    public GameDevDecodeException()
        : base("The game development file is recognized but cannot be decoded safely.")
    {
    }

    public GameDevDecodeException(Exception innerException)
        : base("The game development file is recognized but cannot be decoded safely.", innerException)
    {
    }
}
