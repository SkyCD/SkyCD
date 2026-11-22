using System;

namespace SkyCD.Plugin.Ai.Description.Exceptions;

public sealed class AcpRegistryException(string reason) : InvalidOperationException($"ACP registry unavailable: {reason}");
