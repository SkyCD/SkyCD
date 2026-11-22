using System;
using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Ai.Description.Exceptions;

namespace SkyCD.Plugin.Ai.Description.Models;

public sealed record DescriptionAgentOptions
{
    public bool Enabled { get; init; }
    public string Executable { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public string? AuthMethodId { get; init; }
    public string? Model { get; init; }
    public string Language { get; init; } = "English";
    public string Prompt { get; init; } = "Write a concise factual description of this file. Do not invent facts.";
    public string PromptVersion { get; init; } = "1";
    public string ProcessingDestination { get; init; } = "unknown";
    public bool AllowMetadataOnlyInference { get; init; }
    public int MaxOutputCharacters { get; init; } = 2000;
    public int TimeoutSeconds { get; init; } = 60;
    public DescriptionContextOptions Sources { get; init; } = new();

    public void Validate()
    {
        if (!Enabled) throw new DescriptionAgentException("generation is disabled; enable the configured ACP agent explicitly");
        ValidateSettings();
    }

    internal void ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(Executable) || !System.IO.Path.IsPathFullyQualified(Executable) || !System.IO.File.Exists(Executable))
            throw new DescriptionAgentException("configure an absolute path to an installed ACP executable");
        if (Prompt is null || Sources is null || Arguments is null || Environment is null
            || Arguments.Count > 128 || Environment.Count > 64
            || MaxOutputCharacters is < 1 or > 8000 || TimeoutSeconds is < 1 or > 300
            || Prompt.Length > 8000 || PromptVersion is null || PromptVersion.Length > 128
            || Model?.Length > 256 || AuthMethodId?.Length > 128
            || string.IsNullOrWhiteSpace(Language) || Language.Length > 100
            || ProcessingDestination is not ("unknown" or "local-inference" or "cloud"))
            throw new DescriptionAgentException("invalid language, prompt, destination or generation limits in configuration");
    }
}
