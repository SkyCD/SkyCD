using System;

namespace SkyCD.Presentation.Staging.Exceptions;

public sealed class CatalogScanPublicationException : InvalidOperationException
{
    public CatalogScanPublicationException(string sessionId, string message)
        : base($"Failed to publish scan staging session '{sessionId}': {message}")
    {
    }
}
