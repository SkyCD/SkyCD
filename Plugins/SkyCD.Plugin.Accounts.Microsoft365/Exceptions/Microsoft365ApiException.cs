using System;
using System.Net;

namespace SkyCD.Plugin.Accounts.Microsoft365.Exceptions;

/// <summary>A Microsoft Graph request failed. Provider response bodies are never included.</summary>
public sealed class Microsoft365ApiException : Exception
{
    public Microsoft365ApiException(HttpStatusCode statusCode)
        : base($"Microsoft 365 Graph request failed with status {(int)statusCode}.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
