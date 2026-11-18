using System;

namespace SkyCD.Plugin.AiDescription;

public sealed class AcpRegistryException(string reason) : InvalidOperationException($"ACP registry unavailable: {reason}");
