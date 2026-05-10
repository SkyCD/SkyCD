using DryIoc;

namespace SkyCD.Plugin.Runtime.DependencyInjection;

public interface IServiceRegistrator
{
    void RegisterServices(IRegistrator registrator);
}
