using System;

namespace SkyCD.Cli.Exceptions;

public sealed class CliFormatHandlerMissingException(string extension, string operation)
    : InvalidOperationException($"No format handler registered for '{extension}' ({operation}).");