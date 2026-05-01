using Microsoft.Extensions.DependencyInjection;

namespace SkyCD.Plugin.Runtime.DependencyInjection;

public interface IServiceRegistrator
{
    static abstract void RegisterServices(IServiceCollection services);
}
