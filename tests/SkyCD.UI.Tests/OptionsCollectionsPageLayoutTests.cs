using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
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
            var collectionsList = lists.Single(list => ReferenceEquals(list.ItemsSource, collections.Collections));
            var membersList = lists.Single(list => ReferenceEquals(list.ItemsSource, collections.FilteredMembers));

            var glyphCommands = new ICommand[]
            {
                collections.AddCollectionCommand,
                collections.DeleteCollectionCommand,
                collections.AddSelectedEntriesCommand,
                collections.RequestRemoveSelectedMemberCommand
            };
            var glyphs = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Command is not null &&
                                 glyphCommands.Any(command => ReferenceEquals(command, button.Command)))
                .ToArray();
            Assert.Equal(glyphCommands.Length, glyphs.Length);
            Assert.All(glyphs, button =>
            {
                Assert.True(button.Content is "+" or "-", "Glyph buttons use the shared add/remove content.");
                Assert.Equal(34, button.Width);
                Assert.Equal(28, button.Height);
                Assert.NotNull(button.GetValue(ToolTip.TipProperty));
                Assert.True(button.IsEffectivelyVisible);
            });

            var menuOnlyCommands = new ICommand[]
            {
                collections.RemoveMissingEntriesCommand,
                collections.NavigateToSelectedCommand,
                collections.OpenMemberFolderCommand
            };
            Assert.DoesNotContain(
                window.GetVisualDescendants().OfType<Button>(),
                button => button.Command is not null &&
                          menuOnlyCommands.Any(command => ReferenceEquals(command, button.Command)));

            var collectionMenuItems = collectionsList.ContextMenu?.Items.OfType<MenuItem>().ToArray() ?? [];
            var collectionMenuItem = Assert.Single(collectionMenuItems);
            Assert.Same(collections.RemoveMissingEntriesCommand, collectionMenuItem.Command);
            Assert.False(string.IsNullOrWhiteSpace(collectionMenuItem.Header as string));

            var memberMenuItems = membersList.ContextMenu?.Items.OfType<MenuItem>().ToArray() ?? [];
            Assert.Equal(2, memberMenuItems.Length);
            Assert.Same(collections.NavigateToSelectedCommand, memberMenuItems[0].Command);
            Assert.Same(collections.OpenMemberFolderCommand, memberMenuItems[1].Command);
            Assert.All(memberMenuItems, item => Assert.False(string.IsNullOrWhiteSpace(item.Header as string)));

            var headerTexts = new[]
            {
                LocalizationService.Current.Get(UiKeys.CollectionName),
                LocalizationService.Current.Get(UiKeys.SavedSearch),
                LocalizationService.Current.Get(UiKeys.CollectionEntries)
            };
            var renderedTexts = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(block => block.Text)
                .ToArray();
            Assert.All(headerTexts, header => Assert.Contains(header, renderedTexts));

            var reachable = window.GetVisualDescendants().OfType<Button>()
                    .Select(button => button.Command)
                    .Concat(collectionMenuItems.Select(item => item.Command))
                    .Concat(memberMenuItems.Select(item => item.Command))
                    .ToArray();
            var expectedReachable = new ICommand[]
            {
                collections.AddCollectionCommand,
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
            Assert.All(expectedReachable,
                command => Assert.Contains(reachable, candidate => ReferenceEquals(candidate, command)));
        }
        finally
        {
            window.Close();
        }
    }
}
