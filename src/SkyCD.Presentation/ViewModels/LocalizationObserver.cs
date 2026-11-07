using System;
using System.ComponentModel;
using System.Threading;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

internal static class LocalizationObserver
{
    public static IDisposable Observe<T>(T target, Action<T> refresh) where T : class
    {
        var reference = new WeakReference<T>(target);
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (!reference.TryGetTarget(out var subscriber))
            {
                LocalizationService.Current.PropertyChanged -= handler;
            }
            else if (args.PropertyName == nameof(LocalizationService.CurrentCulture))
            {
                refresh(subscriber);
            }
        };
        LocalizationService.Current.PropertyChanged += handler;
        return new Subscription(() => LocalizationService.Current.PropertyChanged -= handler);
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? release = unsubscribe;
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
