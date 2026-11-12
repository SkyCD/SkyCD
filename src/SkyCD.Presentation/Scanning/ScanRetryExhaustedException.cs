using System;
using System.IO;

namespace SkyCD.Presentation.Scanning;

public sealed class ScanRetryExhaustedException : IOException
{
    public ScanRetryExhaustedException(string path, string stage, int attempts, Exception? innerException = null)
        : base($"Scan operation '{stage}' failed after {attempts} attempt(s) for '{path}'.", innerException)
    {
        Path = path;
        Stage = stage;
        Attempts = attempts;
    }

    public string Path { get; }

    public string Stage { get; }

    public int Attempts { get; }
}
