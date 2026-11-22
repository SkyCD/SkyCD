using System;

namespace SkyCD.Plugin.Ai.Description.Exceptions;

public sealed class DescriptionAgentException(string reason) : InvalidOperationException($"AI description unavailable: {reason}");
