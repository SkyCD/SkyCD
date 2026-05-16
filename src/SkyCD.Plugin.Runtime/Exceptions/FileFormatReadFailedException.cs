using System;

namespace SkyCD.Plugin.Runtime.Exceptions;

public sealed class FileFormatReadFailedException(string? error)
    : InvalidOperationException(error ?? "Read operation failed.");