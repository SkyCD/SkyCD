using SkyCD.Plugin.Abstractions.Capabilities.Accounts;
using SkyCD.Plugin.Accounts.JetBrains;
using SkyCD.Plugin.Accounts.Microsoft365;
using System.Linq;
using Xunit;

namespace SkyCD.Plugin.Accounts.Core.Tests;

public sealed class SyncProviderTests
{
    [Fact]
    public void JetBrains_MapsProductCodes()
    {
        var provider = new JetBrainsSyncProvider();
        var licenses = provider.NormalizeLicenses([
            new ImportedAccountRow { Product = "II", Seats = 1 },
            new ImportedAccountRow { Product = "Custom Tool", Seats = 3 }
        ]);
        Assert.Equal("IntelliJ IDEA Ultimate", licenses[0].Product);
        Assert.Equal("jetbrains:license:intellij-idea-ultimate", licenses[0].Id);
        Assert.Equal("Custom Tool", licenses[1].Product);
        Assert.Empty(provider.NormalizeSubscriptions([new ImportedAccountRow { Product = "II" }]));
    }

    [Fact]
    public void Microsoft365_MapsSkuPartNumbers()
    {
        var provider = new Microsoft365SyncProvider();
        var subscriptions = provider.NormalizeSubscriptions([
            new ImportedAccountRow { Product = "SPE_E5", Seats = 10 },
            new ImportedAccountRow { Product = "Custom Plan", Seats = 1 }
        ]);
        Assert.Equal("Microsoft 365 E5", subscriptions[0].Product);
        Assert.Equal("microsoft365:subscription:microsoft-365-e5", subscriptions[0].Id);
        Assert.Equal("Custom Plan", subscriptions[1].Product);
        Assert.Empty(provider.NormalizeLicenses([new ImportedAccountRow { Product = "SPE_E5" }]));
    }

    [Fact]
    public void Normalize_ClampsSeatsAndTrimsNotes()
    {
        var provider = new JetBrainsSyncProvider();
        var license = provider.NormalizeLicenses([
            new ImportedAccountRow { Product = "WS", Seats = -2, Notes = "  team  " }
        ]).Single();
        Assert.Equal(0, license.Seats);
        Assert.Equal("team", license.Notes);
        Assert.Equal("jetbrains:license:webstorm", license.Id);
    }

    [Fact]
    public void Normalize_UnknownProduct_GetsStableId()
    {
        var first = new JetBrainsSyncProvider().NormalizeLicenses([new ImportedAccountRow { Product = "mystery" }]).Single();
        var second = new JetBrainsSyncProvider().NormalizeLicenses([new ImportedAccountRow { Product = "MYSTERY " }]).Single();
        Assert.Equal(first.Id, second.Id);
    }
}
