using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Ai.Provider.Models;

/// <summary>Local OpenAI-compatible service presets and their shared endpoint conventions.</summary>
public static class AiProviderCatalog
{
    public const string EndpointPath = "/v1";

    /// <summary>Manifest name of <c>Assets/Providers/{id}.png</c>.</summary>
    public static string IconResourceName(string providerId) =>
        "SkyCD.Plugin.Ai.Provider.Assets.Providers." + providerId + ".png";

    public static IReadOnlyList<AiProviderChoice> Providers { get; } =
    [
        Preset("lmstudio", "LM Studio", 1234, "google/gemma-4-e2b", true,
            ["google/gemma-4-e2b", "meta-llama-3.1-8b-instruct", "qwen2.5-7b-instruct"],
            "LM", "https://lmstudio.ai/docs/developer/core/authentication"),
        Preset("ollama", "Ollama", 11434, "llama3.2", true,
            ["llama3.2", "llama3.1", "qwen3", "gemma3", "mistral"], "OL"),
        Preset("colibri", "Colibri", 8000, "glm-5.2", true,
            ["glm-5.2", "qwen3.6", "deepseek-v4-flash"], "CO"),
        Preset("llamacpp", "llama.cpp", 8080, null, false, null, "CPP",
            "https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md#server-specific-params"),
        Preset("koboldcpp", "KoboldCpp", 5001, null, false, null, "KC",
            "https://github.com/LostRuins/koboldcpp/wiki#can-i-add-authentication", hasArtwork: false),
        Preset("jan", "Jan", 1337, null, false, null, "JAN"),
        Preset("localai", "LocalAI", 8080, "llama-3.2-1b-instruct:q4_k_m", false,
            ["llama-3.2-1b-instruct:q4_k_m"], "LA",
            "https://localai.io/docs/administration/authentication/", hasArtwork: false),
        Preset("vllm", "vLLM", 8000, "Qwen/Qwen2.5-1.5B-Instruct", false,
            ["Qwen/Qwen2.5-1.5B-Instruct", "Qwen/Qwen2.5-7B-Instruct"], "VL",
            "https://docs.vllm.ai/en/stable/serving/online_serving/openai_compatible_server/"),
        Preset("custom", "Custom local OpenAI-compatible", 8000, null, false, null, "AI", hasArtwork: false)
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

    private static AiProviderChoice Preset(string id, string name, int port, string? defaultModel, bool canStartServer,
        IReadOnlyList<string>? suggestedModels, string monogram, string? apiKeyHelpUrl = null,
        bool hasArtwork = true) =>
        new(id, name, "http", "127.0.0.1", port, defaultModel, canStartServer, suggestedModels, apiKeyHelpUrl,
            hasArtwork ? IconResourceName(id) : null, monogram);

    /// <summary>Fixed, allowlisted commands used by the Start server action.</summary>
    public static (string Command, string Arguments)? ServerCommand(string providerId) => providerId switch
    {
        "lmstudio" => ("lms", "server start"),
        "ollama" => ("ollama", "serve"),
        "colibri" => ("coli", "serve"),
        _ => null
    };
}
