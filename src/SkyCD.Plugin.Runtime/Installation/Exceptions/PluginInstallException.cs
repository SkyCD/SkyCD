using System;

namespace SkyCD.Plugin.Runtime.Installation.Exceptions;

/// <summary>
/// Base type for staged plugin install failures. Derived types keep a fixed message template and only vary
/// their parameters; <see cref="Reason"/> maps to <see cref="PluginInstallFailureReason"/> for operation state.
/// </summary>
public abstract class PluginInstallException(string message, PluginInstallFailureReason reason)
    : InvalidOperationException(message)
{
    public PluginInstallFailureReason Reason { get; } = reason;
}
