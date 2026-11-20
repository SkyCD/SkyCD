using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.AiProvider;

/// <summary>OpenAI-compatible provider presets and their shared endpoint conventions.</summary>
public static class AiProviderCatalog
{
    public const string EndpointPath = "/v1";

    public static IReadOnlyList<AiProviderChoice> Providers { get; } =
    [
        new("openai", "OpenAI", "https", "api.openai.com", 443, "gpt-4o-mini", CanStartServer: false),
        new("ollama", "Ollama", "http", "127.0.0.1", 11434, "llama3.2", CanStartServer: true),
        new("lmstudio", "LM Studio", "http", "127.0.0.1", 1234, null, CanStartServer: true),
        new("llamacpp", "llama.cpp", "http", "127.0.0.1", 8080, null, CanStartServer: false),
        new("custom", "Custom OpenAI-compatible", "http", "127.0.0.1", 8000, null, CanStartServer: false)
    ];

    public static AiProviderChoice Default => Providers[0];

    public static AiProviderChoice? Find(string? providerId) => Providers.FirstOrDefault(provider =>
        string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase));

    public static string BuildEndpoint(AiProviderSettings settings) =>
        $"{settings.Protocol}://{settings.Address}:{settings.Port}{EndpointPath}";

    public static bool IsLoopback(AiProviderSettings settings) => settings.Address.ToLowerInvariant() switch
    {
        "localhost" or "127.0.0.1" or "::1" or "[::1]" => true,
        _ => false
    };

    /// <summary>Fixed, allowlisted commands used by the Start server action.</summary>
    public static (string Command, string Arguments)? ServerCommand(string providerId) => providerId switch
    {
        "ollama" => ("ollama", "serve"),
        "lmstudio" => ("lms", "server start"),
        _ => null
    };
}
