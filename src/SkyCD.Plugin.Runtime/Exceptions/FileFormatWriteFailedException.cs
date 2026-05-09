using System;

namespace SkyCD.Plugin.Runtime.Exceptions;

public sealed class FileFormatWriteFailedException(string? error)
    : InvalidOperationException(error ?? "Write operation failed.");
