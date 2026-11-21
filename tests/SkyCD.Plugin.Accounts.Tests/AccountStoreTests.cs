using SkyCD.Plugin.Abstractions.Capabilities.Accounts;
using System;
using System.IO;
using Xunit;
using SkyCD.Plugin.Accounts.Models;

namespace SkyCD.Plugin.Accounts.Tests;

public sealed class AccountStoreTests
{
    [Fact]
    public void Load_MissingFile_ReturnsEmptyData()
    {
        var store = new AccountStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "accounts.json"));
        var data = store.Load();
        Assert.Empty(data.Licenses);
        Assert.Empty(data.Subscriptions);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsRecords()
    {
        var path = TempPath();
        var store = new AccountStore(path);
        var data = new AccountData();
        data.Licenses.Add(new LicenseRecord
        {
            Id = "jetbrains:license:intellij-idea-ultimate",
            Provider = "jetbrains",
            Product = "IntelliJ IDEA Ultimate",
            Seats = 2,
            ExpiresUtc = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)
        });
        data.Subscriptions.Add(new SubscriptionRecord
        {
            Id = "microsoft365:subscription:microsoft-365-e5",
            Provider = "microsoft365",
            Product = "Microsoft 365 E5",
            Seats = 10,
            RenewsUtc = new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero),
            Notes = "HQ"
        });
        store.Save(data);

        var reloaded = store.Load();
        var license = Assert.Single(reloaded.Licenses);
        Assert.Equal("IntelliJ IDEA Ultimate", license.Product);
        Assert.Equal(2, license.Seats);
        var subscription = Assert.Single(reloaded.Subscriptions);
        Assert.Equal("HQ", subscription.Notes);
    }

    [Fact]
    public void Upsert_ReplacesByIdAndSorts()
    {
        var store = new AccountStore(TempPath());
        var data = new AccountData();
        var (added, updated) = store.Upsert(data,
            [new LicenseRecord { Id = "b", Provider = "jetbrains", Product = "B" }],
            [new SubscriptionRecord { Id = "a", Provider = "microsoft365", Product = "A" }]);
        Assert.Equal(2, added);
        Assert.Equal(0, updated);

        var (addedAgain, updatedAgain) = store.Upsert(data,
            [new LicenseRecord { Id = "b", Provider = "jetbrains", Product = "B2" }],
            []);
        Assert.Equal(0, addedAgain);
        Assert.Equal(1, updatedAgain);
        Assert.Equal("B2", Assert.Single(data.Licenses).Product);
    }

    [Fact]
    public void EvaluateStatus_UsesDates()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal("active", new LicenseRecord { Id = "a", Provider = "p", Product = "x", ExpiresUtc = now.AddDays(60) }.EvaluateStatus(now));
        Assert.Equal("expiring", new LicenseRecord { Id = "a", Provider = "p", Product = "x", ExpiresUtc = now.AddDays(10) }.EvaluateStatus(now));
        Assert.Equal("expired", new LicenseRecord { Id = "a", Provider = "p", Product = "x", ExpiresUtc = now.AddDays(-1) }.EvaluateStatus(now));
        Assert.Equal("no-expiry", new LicenseRecord { Id = "a", Provider = "p", Product = "x" }.EvaluateStatus(now));
        Assert.Equal("no-renewal", new SubscriptionRecord { Id = "a", Provider = "p", Product = "x" }.EvaluateStatus(now));
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "skycd-accounts-" + Guid.NewGuid().ToString("N"), "accounts.json");
}
