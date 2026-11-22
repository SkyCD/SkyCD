using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Sample.Config.Configuration;

/// <summary>
/// Editable state of the sample configuration surface.
/// </summary>
public sealed class SampleConfigurationEditor
{
    public const string GreetingKey = "greeting";
    public const string SignatureKey = "signature";
    public const string DefaultGreeting = "Hello from SkyCD";
    public const string DefaultSignature = "Sample configuration plugin";

    public SampleConfigurationEditor(IReadOnlyDictionary<string, string?>? currentSettings = null)
    {
        Greeting = Resolve(currentSettings, GreetingKey, DefaultGreeting);
        Signature = Resolve(currentSettings, SignatureKey, DefaultSignature);
    }

    public string Greeting { get; set; }

    public string Signature { get; set; }

    public IReadOnlyDictionary<string, string?> BuildSettings()
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [GreetingKey] = Greeting,
            [SignatureKey] = Signature
        };
    }

    private static string Resolve(IReadOnlyDictionary<string, string?>? currentSettings, string key, string fallback)
    {
        if (currentSettings is null)
        {
            return fallback;
        }

        return currentSettings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }
}
