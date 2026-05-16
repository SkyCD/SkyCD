using System;

namespace SkyCD.Cli.Exceptions;

public sealed class CliRunnerMethodResolutionException(string commandPath)
    : InvalidOperationException($"Could not resolve Run(string[]) for '{commandPath}'.");