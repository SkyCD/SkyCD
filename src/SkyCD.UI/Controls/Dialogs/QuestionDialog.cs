using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SkyCD.UI.Localization;

namespace SkyCD.UI.Controls.Dialogs;

/// <summary>A shared Yes/No dialog. Escape and closing the window both decline.</summary>
public static class QuestionDialog
{
    public static Task<bool> ShowAsync(Window owner, string title, string question)
    {
        var yes = new Button { Content = LocalizationService.Current.Get("core.ui.Yes") };
        var no = new Button { Content = LocalizationService.Current.Get("core.ui.No"), IsCancel = true };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { yes, no }
        };
        buttons.Bind(StackPanel.SpacingProperty, buttons.GetResourceObservable("SkySpaceSm"));
        var content = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 },
                buttons
            }
        };
        content.Bind(StackPanel.MarginProperty, content.GetResourceObservable("SkyInsetMd"));
        content.Bind(StackPanel.SpacingProperty, content.GetResourceObservable("SkySpaceMd"));

        var dialog = new Window
        {
            Title = title,
            Content = content,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        yes.Click += (_, _) => dialog.Close(true);
        no.Click += (_, _) => dialog.Close(false);
        dialog.Opened += (_, _) => no.Focus();
        return dialog.ShowDialog<bool>(owner);
    }
}
