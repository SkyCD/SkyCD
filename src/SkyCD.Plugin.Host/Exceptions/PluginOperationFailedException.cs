using System;

namespace SkyCD.Plugin.Host.Exceptions;

/// <summary>A plugin returned an unsuccessful result without throwing an exception.</summary>
public sealed class PluginOperationFailedException() : Exception("The plugin operation failed.");
