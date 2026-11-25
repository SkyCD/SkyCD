using System.Collections.Generic;
using CommandDotNet;
using SkyCD.SDK.Capabilities.Accounts;
using SkyCD.SDK.Capabilities.Cli;
using Xunit;

namespace SkyCD.Cli.Tests;

public sealed class AccountProviderDispatchTests
{
    [Fact]
    public void RunnerUsesConfiguredInstanceWithAccountProvidersAndJsonOutput()
    {
        var command = new AccountCommand();
        CliHost.ApplyAccountProviders([command], [new IndependentProvider()]);
        CliHost.ApplyJsonOutput(command, true);
        using var registry = new CliContributionRegistry();
        registry.Register([command]);
        var contribution = registry.ResolveCommand(["sync", "independent"], out var consumed);
        Assert.Equal(1, consumed);
        Assert.NotNull(contribution);
        Assert.Equal(0, CliHost.CreateContributionRunner(contribution.CommandInstance).Run(["independent"]));
        Assert.True(command.Executed);
    }

    [Command("sync")]
    public sealed class AccountCommand : ICliPluginCapability, IAccountSyncProviderConsumer, ISupportsJsonOutput
    {
        public IReadOnlyList<IAccountSyncProvider> Providers { get; set; } = [];
        public bool JsonOutput { get; set; }
        public bool Executed { get; private set; }

        [DefaultCommand]
        public int Execute(string providerId)
        {
            Executed = true;
            Assert.True(JsonOutput);
            Assert.Equal(providerId, Assert.Single(Providers).ProviderId);
            return 0;
        }
    }

    public sealed class IndependentProvider : IAccountSyncProvider
    {
        public string ProviderId => "independent";
        public string DisplayName => "Independent";
        public IReadOnlyList<LicenseRecord> NormalizeLicenses(IEnumerable<ImportedAccountRow> rows) => [];
        public IReadOnlyList<SubscriptionRecord> NormalizeSubscriptions(IEnumerable<ImportedAccountRow> rows) => [];
    }
}
