using System;
using System.IO;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Base of every TLS negotiation failure: the requested protection was not established and
/// the connection must not fall back to plaintext. Fatal for the whole scan, never retried.
/// </summary>
public abstract class RemoteTlsException : IOException
{
    private protected RemoteTlsException(string target, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Target = target;
    }

    public string Target { get; }
}
