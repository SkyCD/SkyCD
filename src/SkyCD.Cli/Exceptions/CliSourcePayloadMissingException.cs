using System;

namespace SkyCD.Cli.Exceptions;

public sealed class CliSourcePayloadMissingException()
    : InvalidOperationException("Source format returned empty payload.");