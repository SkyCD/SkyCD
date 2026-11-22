using System;

namespace SkyCD.Plugin.Metadata.AbiWord.Exceptions;

public sealed class AbiWordDecodeException : Exception
{
    public AbiWordDecodeException(string reason, Exception? innerException = null)
        : base($"The AbiWord document could not be read. Reason: {reason}.", innerException)
    {
    }
}
