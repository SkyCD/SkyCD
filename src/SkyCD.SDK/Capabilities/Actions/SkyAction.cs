using System;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Actions;

/// <summary>An executable action for an item. Execution is initiated explicitly by the host.</summary>
/// <param name="Id">Stable provider-scoped action identifier, such as install or launch.</param>
/// <param name="Title">Display title.</param>
/// <param name="ExecuteAsync">Handler accepting host cancellation.</param>
public sealed record SkyAction(string Id, string Title, Func<CancellationToken, Task> ExecuteAsync);
