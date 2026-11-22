using System;

namespace SkyCD.Plugin.Ai.Provider.Exceptions;

public sealed class AiProviderException(string reason) : InvalidOperationException($"AI provider unavailable: {reason}");
