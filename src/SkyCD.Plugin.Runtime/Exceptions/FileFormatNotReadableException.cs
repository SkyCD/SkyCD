using System;

namespace SkyCD.Plugin.Runtime.Exceptions;

public sealed class FileFormatNotReadableException(string formatId)
    : InvalidOperationException($"Format '{formatId}' is not readable.");
