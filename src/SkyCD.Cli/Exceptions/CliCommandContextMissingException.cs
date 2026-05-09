using System;

namespace SkyCD.Cli.Exceptions;

public sealed class CliCommandContextMissingException()
    : InvalidOperationException("CLI command context is missing.");
