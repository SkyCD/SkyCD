using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Rating;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// End-to-end cover for the editable rating: the real Properties window binds the star control to the
/// dialog view model, confirm/cancel decide persistence, and reopening restores the stored rating.
/// </summary>
public class PropertiesWindowRatingTests
{
    [AvaloniaFact]
    public void GeneralTab_ShowsStarControlBoundToTheDraftRating()
    {
        var (window, _, stars) = ShowProperties(new PropertiesCollection { [StarRating.PropertyKey] = "3" });
        try
        {
            Assert.Equal(3, stars.Value);
            Assert.Equal(3, stars.FillLevel);
            Assert.Equal("Rating:", AutomationProperties.GetName(stars));
            Assert.Equal("3 of 5 stars", AutomationProperties.GetHelpText(stars));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == LocalizationService.Current.Get(UiKeys.RatingName));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void UnratedEntry_ShowsNoFilledStars()
    {
        var (window, viewModel, stars) = ShowProperties(new PropertiesCollection { ["FullName"] = "/item" });
        try
        {
            Assert.Equal(StarRating.Unrated, viewModel.Rating);
            Assert.Equal(0, stars.Value);
            Assert.DoesNotContain(stars.Stars, star => star.IsFilled);
            Assert.Equal(LocalizationService.Current.Get(UiKeys.RatingNotSet), AutomationProperties.GetHelpText(stars));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SelectingAStar_UpdatesTheDialogImmediatelyWithoutTouchingTheStoredProperties()
    {
        var source = new PropertiesCollection { [StarRating.PropertyKey] = "2" };
        var (window, viewModel, stars) = ShowProperties(source);
        try
        {
            stars.ApplyStarClick(4);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(4, viewModel.Rating);
            Assert.Equal("4", viewModel.GetProperties()[StarRating.PropertyKey]);
            Assert.Equal("2", source[StarRating.PropertyKey]);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Cancelling_KeepsTheStoredRatingAndReopenRestoresIt()
    {
        var source = new PropertiesCollection { [StarRating.PropertyKey] = "2" };
        var (window, viewModel, stars) = ShowProperties(source);
        try
        {
            stars.ApplyStarClick(5);
            var cancel = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsCancel);
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.False(viewModel.DialogAccepted);
            Assert.Equal("2", source[StarRating.PropertyKey]);
        }
        finally
        {
            window.Close();
        }

        var (reopened, reopenedViewModel, reopenedStars) = ShowProperties(source);
        try
        {
            Assert.Equal(2, reopenedViewModel.Rating);
            Assert.Equal(2, reopenedStars.Value);
        }
        finally
        {
            reopened.Close();
        }
    }

    [AvaloniaFact]
    public void Confirm_CommitsTheSelectedRatingThroughTheAvailableCommand()
    {
        var source = new PropertiesCollection();
        var (window, viewModel, stars) = ShowProperties(source);
        try
        {
            stars.ApplyStarClick(4);
            // The OK button is the window's default action; clicking it runs ConfirmCommand.
            Assert.True(viewModel.ConfirmCommand.CanExecute(null));
            viewModel.ConfirmCommand.Execute(null);

            Assert.True(viewModel.DialogAccepted);
            Assert.Equal("4", viewModel.GetProperties()[StarRating.PropertyKey]);
            // The dialog hands the confirmed properties back to the caller, which persists them.
            source[StarRating.PropertyKey] = viewModel.GetProperties()[StarRating.PropertyKey];
        }
        finally
        {
            window.Close();
        }

        var (reopened, reopenedViewModel, reopenedStars) = ShowProperties(source);
        try
        {
            Assert.Equal(4, reopenedViewModel.Rating);
            Assert.Equal(4, reopenedStars.FillLevel);
        }
        finally
        {
            reopened.Close();
        }
    }

    [AvaloniaFact]
    public void ClearingTheRating_RemovesTheStoredValueOnConfirm_ButNotFromTheSourceUntilThen()
    {
        var source = new PropertiesCollection { [StarRating.PropertyKey] = "3" };
        var (window, viewModel, stars) = ShowProperties(source);
        try
        {
            Assert.True(stars.ApplyStarClick(3));
            Assert.Equal(0, viewModel.Rating);
            viewModel.ConfirmCommand.Execute(null);

            Assert.False(viewModel.GetProperties().ContainsKey(StarRating.PropertyKey));
            Assert.Equal("3", source[StarRating.PropertyKey]);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void KeyboardOnTheFocusedControl_ChangesTheBoundRating()
    {
        var (window, viewModel, stars) = ShowProperties(new PropertiesCollection { [StarRating.PropertyKey] = "2" });
        try
        {
            Assert.True(stars.Focus());
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, viewModel.Rating);

            window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, viewModel.Rating);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StarControl_IsTabReachableAndFocusable()
    {
        var (window, _, stars) = ShowProperties(new PropertiesCollection());
        try
        {
            Assert.True(stars.Focusable);
            var nameField = window.GetVisualDescendants().OfType<TextBox>().First();
            Assert.True(nameField.Focus());
            Dispatcher.UIThread.RunJobs();

            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
            Dispatcher.UIThread.RunJobs();

            Assert.Same(stars, window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RatingRow_IsNotDuplicatedInTheInfoTab()
    {
        var (window, viewModel, _) = ShowProperties(new PropertiesCollection { [StarRating.PropertyKey] = "2" });
        try
        {
            Assert.DoesNotContain(viewModel.InfoProperties.Keys,
                key => string.Equals(key, StarRating.PropertyKey, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HeaderGrid_PlacesEveryChildInsideADeclaredRow()
    {
        var (window, _, _) = ShowProperties(new PropertiesCollection());
        try
        {
            var header = HeaderGrid(window);
            Assert.True(header.RowDefinitions.Count >= 3, "the header must declare a row for the rating line.");
            foreach (var child in header.Children)
            {
                var row = Grid.GetRow(child);
                var span = Math.Max(1, Grid.GetRowSpan(child));
                Assert.InRange(row, 0, header.RowDefinitions.Count - 1);
                Assert.True(row + span <= header.RowDefinitions.Count,
                    $"{child.GetType().Name} spans rows {row}..{row + span - 1}, outside the {header.RowDefinitions.Count} declared rows.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RatingRow_ShowsLabelLeftAndStarsRightAlignedBelowTheNameField()
    {
        var (window, _, stars) = ShowProperties(new PropertiesCollection { [StarRating.PropertyKey] = "3" });
        try
        {
            var content = (Control)window.Content!;
            var header = HeaderGrid(window);
            var icon = header.Children.OfType<Border>().Single();
            var nameFields = window.FindControl<StackPanel>("NameFields")!;
            var ratingRow = window.FindControl<Grid>("RatingRow")!;
            var ratingLabel = window.FindControl<TextBlock>("RatingLabel")!;
            var commentsLabel = window.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == LocalizationService.Current.Get(UiKeys.CommentsLabel));

            // Label and stars are siblings in one row: label in the first column, stars fill the rest.
            Assert.Same(ratingRow, ratingLabel.GetVisualParent());
            Assert.Same(ratingRow, stars.GetVisualParent());
            Assert.Equal(Grid.GetRow(ratingLabel), Grid.GetRow(stars));
            Assert.True(Grid.GetColumn(ratingLabel) < Grid.GetColumn(stars));

            var iconBounds = BoundsRelativeTo(icon, content);
            var nameBounds = BoundsRelativeTo(nameFields, content);
            var rowBounds = BoundsRelativeTo(ratingRow, content);
            var labelBounds = BoundsRelativeTo(ratingLabel, content);
            var starsBounds = BoundsRelativeTo(stars, content);
            var commentsBounds = BoundsRelativeTo(commentsLabel, content);

            // The rating line is its own row below the icon and name field, above the comments.
            Assert.True(rowBounds.Top >= iconBounds.Bottom,
                $"the rating row ({rowBounds.Top}) overlaps the icon ({iconBounds.Bottom}).");
            Assert.True(rowBounds.Top >= nameBounds.Bottom,
                $"the rating row ({rowBounds.Top}) overlaps the name field ({nameBounds.Bottom}).");
            Assert.True(rowBounds.Bottom <= commentsBounds.Top + 1,
                $"the rating row ({rowBounds.Bottom}) overlaps the comments ({commentsBounds.Top}).");

            // Label left, stars right-aligned at the end of the field area, both inside the window.
            Assert.Equal(rowBounds.Left, labelBounds.Left, 1);
            Assert.True(starsBounds.Left >= labelBounds.Right,
                $"the stars ({starsBounds.Left}) are not to the right of the label ({labelBounds.Right}).");
            Assert.Equal(rowBounds.Right, starsBounds.Right, 1);
            Assert.True(starsBounds.Right <= content.Bounds.Width + 1,
                $"the stars ({starsBounds.Right}) are clipped by the window width ({content.Bounds.Width}).");

            // One visual line: label and stars share the same vertical center.
            Assert.Equal(CenterY(labelBounds), CenterY(starsBounds), 1);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("lt")]
    public void RatingRow_FitsTheFixedWindowInEveryLocale(string cultureName)
    {
        var previousCulture = LocalizationService.Current.CurrentCulture;
        try
        {
            LocalizationService.Current.SetCulture(CultureInfo.GetCultureInfo(cultureName));
            var (window, _, stars) = ShowProperties(new PropertiesCollection());
            try
            {
                var content = (Control)window.Content!;
                var ratingRow = window.FindControl<Grid>("RatingRow")!;
                var ratingLabel = window.FindControl<TextBlock>("RatingLabel")!;

                var rowBounds = BoundsRelativeTo(ratingRow, content);
                var labelBounds = BoundsRelativeTo(ratingLabel, content);
                var starsBounds = BoundsRelativeTo(stars, content);

                Assert.Equal(cultureName, LocalizationService.Current.CurrentCulture.Name);
                Assert.True(labelBounds.Width > 0 && starsBounds.Width > 0,
                    $"{cultureName}: the rating line collapsed.");
                Assert.True(labelBounds.Right <= starsBounds.Left + 1,
                    $"{cultureName}: the label overlaps the stars.");
                Assert.True(starsBounds.Right <= content.Bounds.Width + 1,
                    $"{cultureName}: the stars ({starsBounds.Right}) are clipped by the window width ({content.Bounds.Width}).");
                Assert.True(rowBounds.Bottom <= content.Bounds.Height + 1,
                    $"{cultureName}: the rating row ({rowBounds.Bottom}) is clipped by the window height ({content.Bounds.Height}).");
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            LocalizationService.Current.SetCulture(previousCulture);
        }
    }

    private static Grid HeaderGrid(PropertiesWindow window) =>
        (Grid)window.FindControl<StackPanel>("NameFields")!.GetVisualParent()!;

    private static double CenterY(Rect bounds) => bounds.Top + (bounds.Height / 2);

    private static Rect BoundsRelativeTo(Visual visual, Visual ancestor)
    {
        var origin = visual.TranslatePoint(default, ancestor)!.Value;
        return new Rect(origin, visual.Bounds.Size);
    }

    private static (PropertiesWindow Window, PropertiesDialogViewModel ViewModel, StarRatingControl Stars) ShowProperties(
        PropertiesCollection source)
    {
        var viewModel = new PropertiesDialogViewModel("item", "item", "folder", string.Empty, source);
        var window = new PropertiesWindow { DataContext = viewModel, Width = 411, Height = 560 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var stars = Assert.Single(window.GetVisualDescendants().OfType<StarRatingControl>());
        return (window, viewModel, stars);
    }
}
