using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SkyCD.Plugin.Abstractions;

/// <summary>Allowlisted failure metadata. Never contains exception messages or user data.</summary>
public sealed record PluginFailure(string PluginIdentity, string PluginVersion, string Operation, string Fingerprint)
{
    /// <summary>Raised by guarded plugin boundaries; observers must not throw.</summary>
    public static event Action<PluginFailure>? Reported;

    /// <summary>Builds a stable fingerprint without retaining messages, source paths or argument values.</summary>
    public static PluginFailure Create(Type pluginType, string operation, Exception exception)
    {
        var identity = Hash(pluginType.Assembly.GetName().Name ?? "unknown");
        var frames = new StackTrace(exception, false).GetFrames() ?? [];
        var methods = string.Join("\n", frames.Take(8).Select(frame =>
        {
            var method = frame.GetMethod();
            return $"{method?.DeclaringType?.FullName}.{method?.Name}";
        }));
        // Operation is host-controlled; arbitrary plugin strings are never exported.
        var safeOperation = operation is "menu" or "modal" or "read" or "write" ? operation : "plugin";
        return new PluginFailure(identity, pluginType.Assembly.GetName().Version?.ToString() ?? "0.0",
            safeOperation, Hash($"v1\n{identity}\n{safeOperation}\n{exception.GetType().FullName}\n{methods}"));
    }

    /// <summary>Publishes safe metadata without letting reporting failures break plugin isolation.</summary>
    public static void Publish(Type pluginType, string operation, Exception exception)
    {
        var failure = Create(pluginType, operation, exception);
        foreach (var observer in Reported?.GetInvocationList() ?? [])
        {
            try { ((Action<PluginFailure>)observer)(failure); }
            catch { /* Reporting is best effort and must not replace the original failure. */ }
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
