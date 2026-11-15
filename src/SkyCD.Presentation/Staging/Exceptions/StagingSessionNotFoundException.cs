using System;

namespace SkyCD.Presentation.Staging.Exceptions;

public sealed class StagingSessionNotFoundException : InvalidOperationException
{
    public StagingSessionNotFoundException(string sessionId)
        : base($"The scan staging session '{sessionId}' was not found.")
    {
    }
}
