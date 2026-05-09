using System;

namespace SkyCD.Plugin.Runtime.Exceptions;

public sealed class RegistratorMethodMissingException(Type registratorType)
    : InvalidOperationException(
        $"{registratorType.FullName} must expose public static void RegisterServices(IRegistrator registrator).");
