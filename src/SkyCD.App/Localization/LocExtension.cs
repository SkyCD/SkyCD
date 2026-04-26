using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Microsoft.Extensions.DependencyInjection;
using SkyCD.Plugin.Abstractions.Localization;
using PluginServiceProvider = SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider;
using System;

namespace SkyCD.App.Localization;

public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            return string.Empty;
        }

        var i18n = PluginServiceProvider.Instance.GetService<II18nService>();
        return i18n?.Get(Key) ?? Key;
    }
}
