using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SkyCD.SDK.Capabilities.Ui;
using SkyCD.UI.Dialogs;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

public sealed class QuestionDialogTests
{
    [AvaloniaTheory]
    [InlineData(QuestionButtons.Ok, 0, -1, QuestionResult.Ok)]
    [InlineData(QuestionButtons.OkCancel, 0, 1, QuestionResult.Ok, QuestionResult.Cancel)]
    [InlineData(QuestionButtons.YesNo, 0, 1, QuestionResult.Yes, QuestionResult.No)]
    [InlineData(QuestionButtons.YesNoCancel, 0, 2, QuestionResult.Yes, QuestionResult.No, QuestionResult.Cancel)]
    [InlineData(QuestionButtons.SaveDiscardCancel, 0, 2, QuestionResult.Save, QuestionResult.Discard, QuestionResult.Cancel)]
    [InlineData(QuestionButtons.SaveAnywayCancel, 1, 1, QuestionResult.Save, QuestionResult.Cancel)]
    public async Task ButtonsHaveExpectedOrderWidthRolesAndResults(
        QuestionButtons set, int defaultIndex, int cancelIndex, params QuestionResult[] expected)
    {
        var owner = new Window();
        owner.Show();
        try
        {
            for (var index = 0; index < expected.Length; index++)
            {
                var dialog = new QuestionDialog(new QuestionRequest("Title", "Question?", set));
                var decision = dialog.ShowDialog<QuestionResult>(owner);
                Dispatcher.UIThread.RunJobs();

                var buttons = dialog.FindControl<UniformGrid>("Actions")!.Children.Cast<Button>().ToArray();
                Assert.Equal(expected.Length, buttons.Length);
                Assert.Equal(defaultIndex, System.Array.FindIndex(buttons, button => button.IsDefault));
                Assert.Equal(cancelIndex, System.Array.FindIndex(buttons, button => button.IsCancel));
                Assert.All(buttons, button =>
                {
                    Assert.Equal(110, button.MinWidth);
                    Assert.Equal(buttons[0].Bounds.Width, button.Bounds.Width);
                });

                buttons[index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(expected[index], await decision);
            }
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(QuestionButtons.YesNoCancel, PhysicalKey.Enter, QuestionResult.Yes)]
    [InlineData(QuestionButtons.YesNoCancel, PhysicalKey.Escape, QuestionResult.Cancel)]
    [InlineData(QuestionButtons.YesNo, PhysicalKey.Escape, QuestionResult.No)]
    [InlineData(QuestionButtons.Ok, PhysicalKey.Escape, QuestionResult.Cancel)]
    [InlineData(QuestionButtons.SaveAnywayCancel, PhysicalKey.Enter, QuestionResult.Cancel)]
    public async Task KeyboardUsesDialogRolesEvenWhenAnotherActionHasFocus(
        QuestionButtons set, PhysicalKey key, QuestionResult expected)
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var dialog = new QuestionDialog(new QuestionRequest("Title", "Question?", set));
            var decision = dialog.ShowDialog<QuestionResult>(owner);
            Dispatcher.UIThread.RunJobs();
            var buttons = dialog.FindControl<UniformGrid>("Actions")!.Children.Cast<Button>();
            (buttons.FirstOrDefault(button => !button.IsDefault) ?? buttons.First()).Focus();
            dialog.KeyPressQwerty(key, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(decision.IsCompleted);
            Assert.Equal(expected, await decision);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public void ShowsDetailsOnlyWhenProvided()
    {
        var plain = new QuestionDialog(new QuestionRequest("Title", "Question?"));
        Assert.False(plain.FindControl<Border>("DetailsBorder")!.IsVisible);

        string[] details = ["First loss", "Second loss"];
        var warning = new QuestionDialog(new QuestionRequest("Title", "Question?", Details: details));
        Assert.True(warning.FindControl<Border>("DetailsBorder")!.IsVisible);
        Assert.Equal(details, warning.FindControl<ItemsControl>("DetailsList")!.Items.Cast<string>());
    }

    [Theory]
    [InlineData("en", "catalog", "collection")]
    [InlineData("lt", "katalogas", "rinkinys")]
    public void UnsavedChangesWordingIdentifiesWhatWillBeSaved(string language, string catalog, string collection)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo(language);
        Assert.Contains(catalog, LocalizationService.Current.Get(UiKeys.SaveCatalogConfirmation, culture));
        Assert.Contains(collection, LocalizationService.Current.Get(UiKeys.SaveCollectionConfirmation, culture));
    }
}
