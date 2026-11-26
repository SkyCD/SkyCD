using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SkyCD.SDK.Capabilities.Ui;
using SkyCD.UI.Localization;

namespace SkyCD.UI.Dialogs;

/// <summary>Displays a question using the shared SkyCD dialog layout.</summary>
public partial class QuestionDialog : Window
{
    public QuestionDialog(QuestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        InitializeComponent();
        Title = request.Title;
        QuestionText.Text = request.Question;
        DetailsBorder.IsVisible = request.Details is { Count: > 0 };
        DetailsList.ItemsSource = request.Details;

        var ok = (QuestionResult.Ok, UiKeys.Ok);
        var yes = (QuestionResult.Yes, UiKeys.Yes);
        var no = (QuestionResult.No, UiKeys.No);
        var cancel = (QuestionResult.Cancel, UiKeys.Cancel);

        (QuestionResult Result, string Label)[] buttons = request.Buttons switch
        {
            QuestionButtons.Ok => [ok],
            QuestionButtons.OkCancel => [ok, cancel],
            QuestionButtons.YesNo => [yes, no],
            QuestionButtons.YesNoCancel => [yes, no, cancel],
            QuestionButtons.SaveDiscardCancel =>
                [(QuestionResult.Save, UiKeys.Save), (QuestionResult.Discard, UiKeys.Discard), cancel],
            QuestionButtons.SaveAnywayCancel =>
                [(QuestionResult.Save, UiKeys.SaveDataLossSaveAnyway), (QuestionResult.Cancel, UiKeys.SaveDataLossDoNotSave)],
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };

        var safeDefault = request.Buttons == QuestionButtons.SaveAnywayCancel;
        var defaultResult = buttons[safeDefault ? 1 : 0].Result;
        var cancelResult = request.Buttons == QuestionButtons.YesNo ? QuestionResult.No : QuestionResult.Cancel;
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key is Key.Enter or Key.Escape && args.KeyModifiers == KeyModifiers.None)
            {
                args.Handled = true;
                Close(args.Key == Key.Enter ? defaultResult : cancelResult);
            }
        }, RoutingStrategies.Tunnel);
        foreach (var (result, label) in buttons)
        {
            var button = new Button
            {
                Content = LocalizationService.Current.Get(label),
                IsDefault = result == defaultResult,
                IsCancel = result == cancelResult
            };
            button.Click += (_, _) => Close(result);
            Actions.Children.Add(button);
        }
    }

    public static Task<QuestionResult> AskAsync(Window owner, QuestionRequest request) =>
        new QuestionDialog(request).ShowDialog<QuestionResult>(owner);
}
