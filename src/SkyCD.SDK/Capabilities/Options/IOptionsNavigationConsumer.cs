using System;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Options;

/// <summary>Receives navigation to Options pages contributed by the same plugin.</summary>
public interface IOptionsNavigationConsumer : IPluginCapability
{
    /// <summary>Receives a callback that takes a page ID and completes when Options closes.</summary>
    void InitializeOptionsNavigation(Func<string, Task> openPageAsync);
}
