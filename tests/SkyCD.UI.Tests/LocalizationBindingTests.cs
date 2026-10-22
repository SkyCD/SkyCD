using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SkyCD.UI.Localization;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.UI.Tests;

public class LocalizationBindingTests
{
    [AvaloniaFact]
    public void OptionsSelection_UpdatesExistingControlAndCancelRestoresText()
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
            var options = new OptionsDialogViewModel { SelectedTabIndex = 1 };
            var textBlock = new TextBlock();
            var binding = Assert.IsType<Binding>(new TranslateExtension(UiKeys.Cancel).ProvideValue(null!));
            using var subscription = textBlock.Bind(TextBlock.TextProperty, binding);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Cancel", textBlock.Text);

            options.SelectedLanguage = options.Languages.Single(language => language.CultureCode == "lt");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Atšaukti", textBlock.Text);
            Assert.True(options.ShowLanguageSection);

            options.CompleteLanguageSelection(false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Cancel", textBlock.Text);
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
