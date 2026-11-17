using System;
using CommandDotNet.Builders;

namespace SkyCD.Cli.Execution;

internal sealed class CliContributionDependencyResolver(object commandInstance) : IDependencyResolver
{
    public object Resolve(Type type) => type.IsInstanceOfType(commandInstance)
        ? commandInstance
        : Activator.CreateInstance(type)!;

    public bool TryResolve(Type type, out object instance)
    {
        if (type.IsInstanceOfType(commandInstance))
        {
            instance = commandInstance;
            return true;
        }

        instance = null!;
        return false;
    }
}
