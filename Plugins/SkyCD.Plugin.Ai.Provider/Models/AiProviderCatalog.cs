using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Ai.Provider.Models;

/// <summary>Local OpenAI-compatible service presets and their shared endpoint conventions.</summary>
public static class AiProviderCatalog
{
    public const string EndpointPath = "/v1";

    public static IReadOnlyList<AiProviderChoice> Providers { get; } =
    [
        new("lmstudio", "LM Studio", "http", "127.0.0.1", 1234, "google/gemma-4-e2b", CanStartServer: true,
            ["google/gemma-4-e2b", "meta-llama-3.1-8b-instruct", "qwen2.5-7b-instruct"],
            ApiKeyHelpUrl: "https://lmstudio.ai/docs/developer/core/authentication"),
        new("ollama", "Ollama", "http", "127.0.0.1", 11434, "llama3.2", CanStartServer: true,
            ["llama3.2", "llama3.1", "qwen3", "gemma3", "mistral"]),
        new("colibri", "Colibri", "http", "127.0.0.1", 8000, "glm-5.2", CanStartServer: true,
            ["glm-5.2", "qwen3.6", "deepseek-v4-flash"]),
        new("llamacpp", "llama.cpp", "http", "127.0.0.1", 8080, null, CanStartServer: false,
            ApiKeyHelpUrl: "https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md#server-specific-params"),
        new("koboldcpp", "KoboldCpp", "http", "127.0.0.1", 5001, null, CanStartServer: false,
            ApiKeyHelpUrl: "https://github.com/LostRuins/koboldcpp/wiki#can-i-add-authentication"),
        new("jan", "Jan", "http", "127.0.0.1", 1337, null, CanStartServer: false),
        new("localai", "LocalAI", "http", "127.0.0.1", 8080, null, CanStartServer: false,
            ApiKeyHelpUrl: "https://localai.io/docs/administration/authentication/"),
        new("vllm", "vLLM", "http", "127.0.0.1", 8000, null, CanStartServer: false,
            ApiKeyHelpUrl: "https://docs.vllm.ai/en/stable/serving/online_serving/openai_compatible_server/"),
        new("custom", "Custom local OpenAI-compatible", "http", "127.0.0.1", 8000, null, CanStartServer: false)
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
        "lmstudio" => ("lms", "server start"),
        "ollama" => ("ollama", "serve"),
        "colibri" => ("coli", "serve"),
        _ => null
    };
}
