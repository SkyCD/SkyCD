using System;

namespace SkyCD.Plugin.Runtime.Exceptions;

public sealed class FileFormatHandlerResolutionException()
    : InvalidOperationException("Unable to resolve file format handler.");
