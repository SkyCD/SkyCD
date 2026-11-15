using System;

namespace SkyCD.Presentation.Staging.Exceptions;

public sealed class StagingSessionAlreadyClosedException : InvalidOperationException
{
    public StagingSessionAlreadyClosedException(string sessionId)
        : base($"The scan staging session '{sessionId}' has already been closed.")
    {
    }
}
