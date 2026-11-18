using System;

namespace SkyCD.Plugin.Hash.Exceptions;

/// <summary>
/// Thrown when the source cannot be read to completion, so no digest may be published.
/// </summary>
public sealed class HashSourceTruncatedException : Exception
{
    private const string PredefinedMessage = "The source ended after {0} of {1} bytes while hashing.";

    public HashSourceTruncatedException(long receivedBytes, long requestedBytes)
        : base(string.Format(PredefinedMessage, receivedBytes, requestedBytes))
    {
    }
}
