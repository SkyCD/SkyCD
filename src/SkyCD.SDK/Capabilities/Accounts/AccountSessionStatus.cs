#pragma warning disable CS1591

using System;
using System.Collections.Generic;

namespace SkyCD.SDK.Capabilities.Accounts;

/// <summary>One owned license or subscription record, as stored in the connector's token-free snapshot.</summary>
public sealed record AccountOwnedProduct
{
    public required string Id { get; init; }
    public required string Product { get; init; }
    public string? ProductCode { get; init; }
    public string? LicenseType { get; init; }
    public int Seats { get; init; } = 1;
    public DateTimeOffset? ExpiresUtc { get; init; }
    public DateTimeOffset? RenewsUtc { get; init; }
    public bool RenewsAutomatically { get; init; }
    public string? AssigneeEmail { get; init; }
    public required string Status { get; init; }
    public string? ExecutablePath { get; init; }
}

/// <summary>Connection state and owned products of one account connector.</summary>
public sealed record AccountSessionStatus
{
    public required string ProviderId { get; init; }
    public required string DisplayName { get; init; }
    public required string AccountId { get; init; }
    public string? AccountEmail { get; init; }
    public bool IsConnected { get; init; }
    public DateTimeOffset? LastSyncedUtc { get; init; }
    public IReadOnlyList<AccountOwnedProduct> Products { get; init; } = [];
}
