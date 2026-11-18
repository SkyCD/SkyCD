using System;
using System.Net;

namespace SkyCD.Plugin.Accounts.JetBrains;

/// <summary>A JetBrains Account API request failed. Provider response bodies are never included.</summary>
public sealed class JetBrainsApiException : Exception
{
    public JetBrainsApiException(HttpStatusCode statusCode)
        : base($"JetBrains API request failed with status {(int)statusCode}.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
