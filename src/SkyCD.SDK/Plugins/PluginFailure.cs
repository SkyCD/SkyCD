using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using SkyCD.SDK.Lifecycle;

namespace SkyCD.SDK.Plugins;

/// <summary>
/// Allowlisted failure metadata. Never contains exception messages or user data: the extra
/// properties below carry assembly metadata (plugin identity, name, throwing exception type)
/// and a caller-supplied short reason, and the fingerprint inputs are unchanged, so the GitHub
/// diagnostics payload stays as safe as before.
/// </summary>
public sealed record PluginFailure(string PluginIdentity, string PluginVersion, string Operation, string Fingerprint)
{
    /// <summary>Stable plugin id for the local log (assembly metadata, never user data).</summary>
    public string PluginId { get; init; } = PluginIdentity;

    /// <summary>Human-readable plugin name for the local log (assembly metadata, never user data).</summary>
    public string PluginName { get; init; } = PluginIdentity;

    /// <summary>Throwing exception type for the local log (type name only, never the message).</summary>
    public string ExceptionType { get; init; } = "Exception";

    /// <summary>
    /// Short caller-supplied reason for the local log. Excluded from the diagnostics payload,
    /// which keeps the fingerprint only.
    /// </summary>
    [JsonIgnore]
    public string? Reason { get; init; }

    /// <summary>
    /// Affected item display name for the local log only. Excluded from the diagnostics payload
    /// because file names can carry user data; the GitHub report carries the fingerprint instead.
    /// </summary>
    [JsonIgnore]
    public string? ItemName { get; init; }

    /// <summary>
    /// Degraded-but-healthy conditions (a missing optional decoder) render as a warning instead
    /// of an error. Local display only.
    /// </summary>
    [JsonIgnore]
    public bool IsWarning { get; init; }

    /// <summary>Raised by guarded plugin boundaries; observers must not throw.</summary>
    public static event Action<PluginFailure>? Reported;

    /// <summary>Builds a stable fingerprint without retaining messages, source paths or argument values.</summary>
    public static PluginFailure Create(Type pluginType, string operation, Exception exception)
    {
        var assembly = pluginType.Assembly;
        var assemblyName = assembly.GetName().Name ?? "unknown";
        var identity = Hash(assemblyName);
        var frames = new StackTrace(exception, false).GetFrames() ?? [];
        var methods = string.Join("\n", frames.Take(8).Select(frame =>
        {
            var method = frame.GetMethod();
            return $"{method?.DeclaringType?.FullName}.{method?.Name}";
        }));
        // Operation is host-controlled; arbitrary plugin strings are never exported.
        var safeOperation = operation
            is "menu" or "modal" or "read" or "write" or "configure" or "media" or "media-settings"
            or "volume" or "catalog" or "import" or "index" or "assess"
            or "options" or "install" or "uninstall"
            or "audio-cd" or "audio-cd-settings" or "online-metadata"
            ? operation
            : "plugin";
        return new PluginFailure(identity, assembly.GetName().Version?.ToString() ?? "0.0",
            safeOperation, Hash($"v1\n{identity}\n{safeOperation}\n{exception.GetType().FullName}\n{methods}"))
        {
            PluginId = assembly.GetCustomAttribute<PluginIdAttribute>()?.Id ?? assemblyName,
            PluginName = assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? assemblyName,
            ExceptionType = exception.GetType().Name,
        };
    }

    /// <summary>Publishes safe metadata without letting reporting failures break plugin isolation.</summary>
    public static void Publish(Type pluginType, string operation, Exception exception)
    {
        Publish(pluginType, operation, exception, pluginId: null);
    }

    /// <summary>
    /// Publishes safe metadata with host-known routing details. The plugin id, reason, item name
    /// and warning flag only enrich the local log; the fingerprint (and therefore the GitHub
    /// payload) keeps the same inputs as <see cref="Create(Type, string, Exception)"/>.
    /// </summary>
    public static void Publish(Type pluginType, string operation, Exception exception, string? pluginId,
        string? reason = null, string? itemName = null, bool isWarning = false)
    {
        var failure = Create(pluginType, operation, exception);
        if (!string.IsNullOrWhiteSpace(pluginId))
        {
            failure = failure with { PluginId = pluginId.Trim() };
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            failure = failure with { Reason = reason.Trim() };
        }

        if (!string.IsNullOrWhiteSpace(itemName))
        {
            failure = failure with { ItemName = itemName.Trim() };
        }

        if (isWarning)
        {
            failure = failure with { IsWarning = true };
        }

        foreach (var observer in Reported?.GetInvocationList() ?? [])
        {
            try { ((Action<PluginFailure>)observer)(failure); }
            catch { /* Reporting is best effort and must not replace the original failure. */ }
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
