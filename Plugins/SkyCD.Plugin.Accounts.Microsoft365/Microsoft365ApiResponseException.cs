using System;

namespace SkyCD.Plugin.Accounts.Microsoft365;

/// <summary>Microsoft Graph returned a payload that is not a subscription list.</summary>
public sealed class Microsoft365ApiResponseException : Exception
{
    public Microsoft365ApiResponseException()
        : base("Microsoft 365 Graph returned an invalid subscription response.")
    {
    }
}
