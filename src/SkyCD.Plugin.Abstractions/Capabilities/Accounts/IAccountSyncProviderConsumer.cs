using System.Collections.Generic;

namespace SkyCD.Plugin.Abstractions.Capabilities.Accounts;

public interface IAccountSyncProviderConsumer
{
    IReadOnlyList<IAccountSyncProvider> Providers { get; set; }
}
