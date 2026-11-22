using System;

namespace SkyCD.Plugin.Metadata.Zoo.Exceptions;

/// <summary>
/// Raised when an entry uses a ZOO packing method this plugin cannot decompress.
/// </summary>
public sealed class ZooUnsupportedMethodException(int packingMethod)
    : NotSupportedException($"ZOO entry uses unsupported packing method {packingMethod}.");
