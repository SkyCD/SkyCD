using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

public class LocalizationBindingTests
{
    [AvaloniaFact]
    public void TranslateExtension_UpdatesExistingControlWhenCultureChanges()
    {
        var service = LocalizationService.Current;
        var originalServiceCulture = service.CurrentCulture;
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            service.SetCulture(CultureInfo.GetCultureInfo("en"));
            var textBlock = new TextBlock();
            var binding = Assert.IsType<Binding>(new TranslateExtension(UiKeys.Cancel).ProvideValue(null!));
            using var subscription = textBlock.Bind(TextBlock.TextProperty, binding);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Cancel", textBlock.Text);

            service.SetCulture(CultureInfo.GetCultureInfo("lt"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Atšaukti", textBlock.Text);
        }
        finally
        {
            service.SetCulture(originalServiceCulture);
            Dispatcher.UIThread.RunJobs();
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
        }
    }
}
