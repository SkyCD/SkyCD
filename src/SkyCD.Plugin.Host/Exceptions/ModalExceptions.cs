using System;

namespace SkyCD.Plugin.Host.Exceptions;

public sealed class ModalCapabilityNotFoundException(string modalId)
    : InvalidOperationException($"No plugin capability found for modal '{modalId}'.");
