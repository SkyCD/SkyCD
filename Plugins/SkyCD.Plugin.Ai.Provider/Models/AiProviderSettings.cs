using System.Collections.Generic;

namespace SkyCD.Plugin.Ai.Provider.Models;

/// <summary>One provider preset offered by the AI provider plugin.</summary>
/// <param name="Id">Stable provider identifier.</param>
/// <param name="Name">User-facing provider name.</param>
/// <param name="Protocol">Default connection protocol, <c>http</c> or <c>https</c>.</param>
/// <param name="Address">Default host name or address.</param>
/// <param name="Port">Default TCP port.</param>
/// <param name="DefaultModel">Model suggested when the provider is first selected.</param>
/// <param name="CanStartServer">Whether the plugin can start a local provider server.</param>
/// <param name="SuggestedModels">Models to offer when this provider is selected.</param>
/// <param name="ApiKeyHelpUrl">
/// Page that explains authentication and how to obtain an API key for this provider;
/// <c>null</c> when the provider has no key concept.
/// </param>
/// <param name="IconKey">
/// Packaged icon key (<c>llm-backend:</c> prefix) resolved by the plugin page;
/// empty when the preset has no artwork and the page draws <paramref name="Monogram"/>.
/// </param>
/// <param name="Monogram">One to three letters drawn when <paramref name="IconKey"/> does not load.</param>
public sealed record AiProviderChoice(string Id, string Name, string Protocol, string Address, int Port,
    string? DefaultModel, bool CanStartServer, IReadOnlyList<string>? SuggestedModels = null,
    string? ApiKeyHelpUrl = null, string? IconKey = null, string Monogram = "");

/// <summary>Persisted provider connection and generation settings.</summary>
public sealed record AiProviderSettings
{
    /// <summary>Selected provider preset id.</summary>
    public string ProviderId { get; init; } = "ollama";

    /// <summary>Connection protocol, <c>http</c> or <c>https</c>.</summary>
    public string Protocol { get; init; } = "http";

    /// <summary>Host name or address without scheme, port or path.</summary>
    public string Address { get; init; } = "127.0.0.1";

    /// <summary>TCP port of the OpenAI-compatible endpoint.</summary>
    public int Port { get; init; } = 11434;

    /// <summary>Optional bearer token; empty for unauthenticated local providers.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Model identifier sent with each request.</summary>
    public string Model { get; init; } = "llama3.2";

    /// <summary>Requested output language.</summary>
    public string Language { get; init; } = "English";

    /// <summary>Whether cached images larger than 448 × 448 are downscaled before sending.</summary>
    public bool ResizeLargeImages { get; init; } = true;

    /// <summary>Whether generation is allowed; saving from the configuration page enables it.</summary>
    public bool Enabled { get; init; }

    /// <summary>Maximum description length in characters.</summary>
    public int MaxOutputCharacters { get; init; } = 2000;

    /// <summary>HTTP request timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 90;

    /// <summary>Instruction sent with every generation request.</summary>
    public string Prompt { get; init; } = "Write a concise factual description of this file. Do not invent facts.";
}

/// <summary>Outcome of a provider connection check, including the models reported by the provider.</summary>
public sealed record AiProviderConnectionResult(bool IsConnected, string Message,
    IReadOnlyList<string> Models);
