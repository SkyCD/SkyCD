using System;

namespace SkyCD.Plugin.Accounts.JetBrains.Exceptions;

/// <summary>The JetBrains Account API returned a payload that is not a license list.</summary>
public sealed class JetBrainsApiResponseException : Exception
{
    public JetBrainsApiResponseException()
        : base("JetBrains API returned an invalid license response.")
    {
    }
}
