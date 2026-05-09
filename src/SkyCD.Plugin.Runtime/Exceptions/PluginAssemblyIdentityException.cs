using System;

namespace SkyCD.Plugin.Runtime.Exceptions;

public sealed class PluginAssemblyIdentityException(string assemblyName)
    : InvalidOperationException($"Assembly '{assemblyName}' does not provide a valid plugin identity.");
