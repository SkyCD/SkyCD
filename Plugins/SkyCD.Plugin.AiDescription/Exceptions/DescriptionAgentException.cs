using System;

namespace SkyCD.Plugin.AiDescription.Exceptions;

public sealed class DescriptionAgentException(string reason) : InvalidOperationException($"AI description unavailable: {reason}");
