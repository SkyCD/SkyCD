using System;

namespace SkyCD.Plugin.Runtime.Exceptions;

public sealed class PluginAssemblyInspectionException(string assemblyName, Exception innerException)
    : InvalidOperationException($"Failed to inspect assembly '{assemblyName}'.", innerException);
