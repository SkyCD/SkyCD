using System;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.AiProvider;

/// <summary>Validates provider settings before they are persisted or used for a request.</summary>
public static class AiProviderSettingsValidator
{
    public static void Validate(AiProviderSettings settings, bool requireEnabled)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (requireEnabled && !settings.Enabled)
        {
            throw new AiProviderException("generation is disabled; configure and save the AI provider in Options");
        }

        if (AiProviderCatalog.Find(settings.ProviderId) is null)
        {
            throw new AiProviderException("select a supported provider");
        }

        if (settings.Protocol is not ("http" or "https"))
        {
            throw new AiProviderException("the protocol must be http or https");
        }

        if (string.IsNullOrWhiteSpace(settings.Address) || settings.Address.Length > 253 ||
            settings.Address.IndexOfAny(['/', '\\', ':', ' ', '\t']) >= 0 ||
            settings.Address.StartsWith('.') || settings.Address.EndsWith('.'))
        {
            throw new AiProviderException("enter a host name or address without a protocol, port or path");
        }

        if (settings.Port is < 1 or > 65535)
        {
            throw new AiProviderException("the port must be between 1 and 65535");
        }

        if (settings.ApiKey is null || settings.ApiKey.Length > 8192)
        {
            throw new AiProviderException("the API key is not valid");
        }

        if (string.IsNullOrWhiteSpace(settings.Model) || settings.Model.Length > 256)
        {
            throw new AiProviderException("enter or select a model");
        }

        if (string.IsNullOrWhiteSpace(settings.Language) || settings.Language.Length > 100)
        {
            throw new AiProviderException("enter an output language");
        }

        if (settings.MaxOutputCharacters is < 1 or > 8000)
        {
            throw new AiProviderException("the output limit must be between 1 and 8000 characters");
        }

        if (settings.TimeoutSeconds is < 5 or > 300)
        {
            throw new AiProviderException("the timeout must be between 5 and 300 seconds");
        }

        if (string.IsNullOrWhiteSpace(settings.Prompt) || settings.Prompt.Length > 8000)
        {
            throw new AiProviderException("the prompt is not valid");
        }

        if (settings.Protocol == "https" && settings.Port == 80)
        {
            throw new AiProviderException("port 80 is not used with https");
        }
    }
}
