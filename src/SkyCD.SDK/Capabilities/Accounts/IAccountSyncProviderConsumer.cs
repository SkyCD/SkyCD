#pragma warning disable CS1591

using System.Collections.Generic;

namespace SkyCD.SDK.Capabilities.Accounts;

public interface IAccountSyncProviderConsumer
{
    IReadOnlyList<IAccountSyncProvider> Providers { get; set; }
}
