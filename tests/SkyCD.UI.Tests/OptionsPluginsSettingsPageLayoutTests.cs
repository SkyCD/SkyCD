using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issue #1055: the Plugins → Settings repository list renders like the other Options lists — list
/// actions aligned with the bordered list, editable user rows and a protected built-in row.
/// </summary>
public sealed class OptionsPluginsSettingsPageLayoutTests : IDisposable
{
    private readonly AppDataSandbox sandbox = new();

    public void Dispose() => sandbox.Dispose();

    [AvaloniaFact]
    public void RepositoryList_AlignsListActionsWithTheListAndProtectsTheBuiltInRow()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPluginRepositories(["https://repo.example/plugins.json"]);
        viewModel.SelectedSettingCategory = viewModel.SettingCategories
            .Single(category => category.Id == "plugins-settings");
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.ShowPluginsSettingsSection);

        var window = new OptionsWindow { DataContext = viewModel };
        window.Show();
        window.Width = 780;
        window.Height = 620;
        Dispatcher.UIThread.RunJobs();

        try
        {
            var list = window.GetVisualDescendants().OfType<ListBox>()
                .Single(control => ReferenceEquals(control.ItemsSource, viewModel.PluginRepositories));
            var container = list.GetVisualAncestors().OfType<Border>().First();
            var section = list.GetVisualAncestors().OfType<StackPanel>().First();
            var buttons = section.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Content is "+" or "-")
                .ToArray();
            Assert.Equal(2, buttons.Length);

            var containerRight = Right(container, window);
            var actions = buttons[0].GetVisualAncestors().OfType<StackPanel>().First();
            Assert.InRange(Math.Abs(Right(actions, window) - containerRight), 0, 1.5);
            foreach (var button in buttons)
            {
                Assert.True(button.Bounds.Width > 0, "Repository list actions must be visible.");
            }

            Assert.True(list.Bounds.Width > 0, "The repository list must be visible.");
            Assert.True(list.Bounds.Height > 0);

            var rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.Equal(2, rows.Length);
            Assert.True(rows[0].GetVisualDescendants().OfType<TextBox>().Single().IsReadOnly,
                "The built-in catalog row must stay read-only.");
            Assert.False(rows[1].GetVisualDescendants().OfType<TextBox>().Single().IsReadOnly,
                "User repository rows must stay editable.");

            var remove = buttons.Single(button => button.Content is "-");
            Assert.False(remove.IsEffectivelyEnabled,
                "Remove must be disabled while the built-in row is selected.");

            viewModel.SelectedPluginRepository = viewModel.PluginRepositories[1];
            Dispatcher.UIThread.RunJobs();
            Assert.True(remove.IsEffectivelyEnabled,
                "Remove must enable once a user row is selected.");
        }
        finally
        {
            window.Close();
        }
    }

    private static double Right(Visual visual, Visual relativeTo) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width, 0), relativeTo)!.Value.X;
}