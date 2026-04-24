namespace SkyCD.Plugin.Runtime.DependencyInjection;

/// <summary>
/// Global process-level access to the current plugin service provider.
/// </summary>
public static class GlobalPluginServiceProvider
{
    private static readonly object Sync = new();
    private static IServiceProvider current = EmptyServiceProvider.Instance;
    private static IDisposable? currentDisposable;

    public static IServiceProvider Current
    {
        get
        {
            lock (Sync)
            {
                return current;
            }
        }
    }

    public static void Set(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (Sync)
        {
            if (ReferenceEquals(current, provider))
            {
                return;
            }

            currentDisposable?.Dispose();
            current = provider;
            currentDisposable = provider as IDisposable;
        }
    }

    public static void Reset()
    {
        lock (Sync)
        {
            currentDisposable?.Dispose();
            current = EmptyServiceProvider.Instance;
            currentDisposable = null;
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
