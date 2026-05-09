using System;

namespace SkyCD.Cli.Exceptions;

public sealed class CliFormatOperationNotSupportedException(string formatId, string operation)
    : InvalidOperationException($"Format '{formatId}' does not support {operation}.");
