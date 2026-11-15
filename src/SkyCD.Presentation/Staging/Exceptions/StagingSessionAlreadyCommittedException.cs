using System;

namespace SkyCD.Presentation.Staging.Exceptions;

public sealed class StagingSessionAlreadyCommittedException : InvalidOperationException
{
    public StagingSessionAlreadyCommittedException(string sessionId)
        : base($"The scan staging session '{sessionId}' has already been committed.")
    {
    }
}
