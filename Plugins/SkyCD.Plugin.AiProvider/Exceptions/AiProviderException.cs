using System;

namespace SkyCD.Plugin.AiProvider.Exceptions;

public sealed class AiProviderException(string reason) : InvalidOperationException($"AI provider unavailable: {reason}");
