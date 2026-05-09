using System;

namespace SkyCD.Plugin.Runtime.Exceptions;

public sealed class PluginAssemblyCapabilitiesMissingException(string assemblyName)
    : InvalidOperationException($"Assembly '{assemblyName}' does not expose plugin capabilities.");
