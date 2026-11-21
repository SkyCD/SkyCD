using System;

namespace SkyCD.Plugin.Runtime.Installation.Exceptions;

/// <summary>The plugin files are in use or the install, staging or backup location cannot be written.</summary>
public sealed class PluginInstallDestinationException : PluginInstallException
{
    public PluginInstallDestinationException(string pluginId, string detail, Exception? innerException = null)
        : base($"Plugin '{pluginId}' could not be installed: {detail}.", PluginInstallFailureReason.DestinationUnavailable)
    {
        Cause = innerException;
    }

    /// <summary>The underlying IO failure, retained for diagnostics only.</summary>
    public Exception? Cause { get; }
}
