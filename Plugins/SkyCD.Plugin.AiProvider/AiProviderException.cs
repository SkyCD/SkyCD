using System;

namespace SkyCD.Plugin.AiProvider;

public sealed class AiProviderException(string reason) : InvalidOperationException($"AI provider unavailable: {reason}");
