using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.UI.Tests;

public sealed class OptionsCollectionsPageLayoutTests
{
    [AvaloniaFact]
    public void CollectionsPage_RendersBothListsAndEveryManagerAction()
    {
        var collections = new CollectionsDialogViewModel();
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetCollections(collections);
        viewModel.SelectedSettingCategory = viewModel.SettingCategories
            .Single(category => category.Id == OptionsDialogViewModel.CollectionsCategoryId);
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.ShowCollectionsSection);

        var window = new OptionsWindow { DataContext = viewModel };
        window.Show();
        window.Width = 780;
        window.Height = 620;
        Dispatcher.UIThread.RunJobs();

        try
        {
            var lists = window.GetVisualDescendants().OfType<ListBox>()
                .Where(list => ReferenceEquals(list.ItemsSource, collections.Collections) ||
                               ReferenceEquals(list.ItemsSource, collections.FilteredMembers))
                .ToArray();
            Assert.Equal(2, lists.Length);
            Assert.All(lists, list => Assert.True(list.IsEffectivelyVisible));

            var actions = new[]
            {
                collections.CreateCollectionCommand,
                collections.DeleteCollectionCommand,
                collections.RemoveMissingEntriesCommand,
                collections.NavigateToSelectedCommand,
                collections.OpenMemberFolderCommand,
                collections.AddSelectedEntriesCommand,
                collections.RequestRemoveSelectedMemberCommand,
                collections.ConfirmRemoveSelectedMemberCommand,
                collections.CancelRemoveMemberCommand,
                collections.ConfirmDeleteCollectionCommand,
                collections.CancelDeleteCollectionCommand
            };
            var buttons = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Command is not null &&
                                 actions.Any(action => ReferenceEquals(action, button.Command)))
                .ToArray();

            // The confirmation bars start collapsed, so their buttons exist but measure zero until shown.
            var barCommands = new[]
            {
                collections.ConfirmRemoveSelectedMemberCommand,
                collections.CancelRemoveMemberCommand,
                collections.ConfirmDeleteCollectionCommand,
                collections.CancelDeleteCollectionCommand
            };
            var visible = buttons
                .Where(button => !barCommands.Any(action => ReferenceEquals(action, button.Command)))
                .ToArray();

            Assert.Equal(actions.Length, buttons.Length);
            Assert.Equal(actions.Length - barCommands.Length, visible.Length);
            Assert.All(visible, button => Assert.True(button.Bounds.Width > 0, "Manager actions must be visible."));
            Assert.All(buttons, button => Assert.NotNull(button.GetValue(ToolTip.TipProperty)));
            Assert.All(buttons, button => Assert.False(string.IsNullOrWhiteSpace(button.Content as string)));
        }
        finally
        {
            window.Close();
        }
    }
}
