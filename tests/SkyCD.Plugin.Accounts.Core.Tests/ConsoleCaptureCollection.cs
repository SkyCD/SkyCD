using Xunit;

namespace SkyCD.Plugin.Accounts.Core.Tests;

/// <summary>
/// Console-capturing CLI tests share process-wide standard output and must not run in parallel.
/// </summary>
[CollectionDefinition("Console capture", DisableParallelization = true)]
public sealed class ConsoleCaptureCollection
{
}
