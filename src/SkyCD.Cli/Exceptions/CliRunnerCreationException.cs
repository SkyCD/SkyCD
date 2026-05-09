using System;

namespace SkyCD.Cli.Exceptions;

public sealed class CliRunnerCreationException(string commandPath)
    : InvalidOperationException($"Failed to create CLI runner for '{commandPath}'.");
