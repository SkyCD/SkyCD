namespace SkyCD.Plugin.Abstractions.Capabilities.Accounts;

/// <summary>Result of connecting a provider account. Never contains credentials.</summary>
public sealed class AccountConnectionResult
{
    /// <summary>Gets whether the account was connected successfully.</summary>
    public required bool Success { get; init; }

    /// <summary>Gets the provider's account identifier when connected.</summary>
    public string? AccountId { get; init; }

    /// <summary>Gets an optional failure message.</summary>
    public string? Error { get; init; }
}
