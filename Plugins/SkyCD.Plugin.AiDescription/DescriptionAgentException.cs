using System;

namespace SkyCD.Plugin.AiDescription;

public sealed class DescriptionAgentException(string reason) : InvalidOperationException($"AI description unavailable: {reason}");
