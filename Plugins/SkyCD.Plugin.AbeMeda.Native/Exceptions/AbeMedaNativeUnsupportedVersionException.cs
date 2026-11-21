using System;
using SkyCD.Plugin.AbeMeda.Native.Models;

namespace SkyCD.Plugin.AbeMeda.Native.Exceptions;

/// <summary>
/// Thrown when the native catalog reports a version outside the verified policy.
/// </summary>
public sealed class AbeMedaNativeUnsupportedVersionException(uint version, uint compatibilityVersion)
    : InvalidOperationException(
        $"Unsupported abeMeda/NeoFinder native catalog version {version} (compatibility marker {compatibilityVersion}). " +
        $"Supported version: {AbeMedaNativeLayout.SupportedVersion}.")
{
}
