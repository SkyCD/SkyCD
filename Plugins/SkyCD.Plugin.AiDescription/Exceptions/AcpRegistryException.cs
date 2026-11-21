using System;

namespace SkyCD.Plugin.AiDescription.Exceptions;

public sealed class AcpRegistryException(string reason) : InvalidOperationException($"ACP registry unavailable: {reason}");
