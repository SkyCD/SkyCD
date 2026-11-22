using System;

namespace SkyCD.Plugin.Metadata.Text.Exceptions;

/// <summary>
/// Exception thrown when a source claims a text extension but its bytes are binary.
/// </summary>
/// <remarks>
/// Rejection is deliberate: decoding arbitrary bytes as text produces mojibake that pollutes the catalog and
/// matches searches on nonsense. A separate type from <see cref="TextDecodeException"/> keeps "not text"
/// distinguishable from "text we could not parse".
/// </remarks>
public sealed class BinaryContentRejectedException : Exception
{
    private const string PredefinedMessage =
        "The source is binary rather than text; {0} of the first {1} inspected bytes are non-text control characters.";

    public BinaryContentRejectedException(int controlBytes, int inspectedBytes)
        : base(string.Format(PredefinedMessage, controlBytes, inspectedBytes))
    {
    }
}
