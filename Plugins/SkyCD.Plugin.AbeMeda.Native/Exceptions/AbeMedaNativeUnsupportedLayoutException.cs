using System;

namespace SkyCD.Plugin.AbeMeda.Native.Exceptions;

/// <summary>
/// Thrown when the header declares a layout that differs from the verified profile.
/// </summary>
public sealed class AbeMedaNativeUnsupportedLayoutException(string detail)
    : InvalidOperationException($"Native abeMeda/NeoFinder catalog layout is not supported: {detail}.")
{
}
