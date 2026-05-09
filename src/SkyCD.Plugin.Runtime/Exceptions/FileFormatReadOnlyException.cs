using System;

namespace SkyCD.Plugin.Runtime.Exceptions;

public sealed class FileFormatReadOnlyException(string formatId)
    : InvalidOperationException($"Format '{formatId}' is read-only.");
