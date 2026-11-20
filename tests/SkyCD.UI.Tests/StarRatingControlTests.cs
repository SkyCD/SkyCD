using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SkyCD.UI.Controls.Rating;
using Xunit;

namespace SkyCD.UI.Tests;

public class StarRatingControlTests
{
    [AvaloniaFact]
    public void NewControl_IsUnratedAndFocusable()
    {
        var control = new StarRatingControl();

        Assert.Equal(0, control.Value);
        Assert.Equal(0, control.FillLevel);
        Assert.True(control.Focusable);
        Assert.Equal(5, control.Stars.Count);
        Assert.Equal([1, 2, 3, 4, 5], control.Stars.Select(star => star.StarIndex));
    }

    [AvaloniaTheory]
    [InlineData(0, 3)]
    [InlineData(2, 3)]
    [InlineData(5, 3)]
    [InlineData(-7, 3)]
    [InlineData(9, 3)]
    public void ApplyStarClick_SelectsClickedStar(int currentValue, int clickedStar)
    {
        var control = new StarRatingControl { Value = currentValue };

        Assert.True(control.ApplyStarClick(clickedStar));

        Assert.Equal(3, control.Value);
        Assert.Equal(3, control.FillLevel);
    }

    [AvaloniaFact]
    public void ApplyStarClick_OnSelectedStar_ClearsRating()
    {
        var control = new StarRatingControl { Value = 4 };

        control.ApplyStarClick(4);

        Assert.Equal(0, control.Value);
        Assert.DoesNotContain(control.Stars, star => star.IsFilled);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void ApplyStarClick_IgnoresStarsOutsideTheRange(int starIndex)
    {
        var control = new StarRatingControl { Value = 2 };

        Assert.False(control.ApplyStarClick(starIndex));

        Assert.Equal(2, control.Value);
    }

    [AvaloniaFact]
    public void ApplyValue_RaisesValueSubmittedOnce()
    {
        var control = new StarRatingControl();
        var raised = 0;
        control.ValueSubmitted += (_, _) => raised++;

        control.ApplyValue(5);

        Assert.Equal(5, control.Value);
        Assert.Equal(1, raised);
    }

    [AvaloniaFact]
    public void Value_IsClampedToFiveStars()
    {
        var control = new StarRatingControl();

        control.Value = 42;
        Assert.Equal(5, control.Value);

        control.Value = -3;
        Assert.Equal(0, control.Value);
    }

    [AvaloniaTheory]
    [InlineData(0, Key.Right, 1)]
    [InlineData(3, Key.Right, 4)]
    [InlineData(5, Key.Right, 5)]
    [InlineData(0, Key.Left, 0)]
    [InlineData(2, Key.Left, 1)]
    [InlineData(2, Key.Up, 3)]
    [InlineData(2, Key.Down, 1)]
    [InlineData(3, Key.Home, 0)]
    [InlineData(3, Key.Delete, 0)]
    [InlineData(3, Key.Back, 0)]
    [InlineData(0, Key.End, 5)]
    [InlineData(0, Key.Space, 1)]
    [InlineData(2, Key.Space, 3)]
    [InlineData(5, Key.Space, 0)]
    [InlineData(2, Key.Enter, 3)]
    public void KeyboardMap_ChangesTheRating(int currentValue, Key key, int expected)
    {
        Assert.Equal(expected, StarRatingControl.GetValueForKey(currentValue, key, KeyModifiers.None));
    }

    [AvaloniaTheory]
    [InlineData(Key.A)]
    [InlineData(Key.Tab)]
    [InlineData(Key.Escape)]
    public void KeyboardMap_IgnoresUnrelatedKeys(Key key)
    {
        Assert.Null(StarRatingControl.GetValueForKey(2, key, KeyModifiers.None));
    }

    [AvaloniaTheory]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Meta)]
    public void KeyboardMap_LeavesModifiedKeysToTheDialog(KeyModifiers modifiers)
    {
        Assert.Null(StarRatingControl.GetValueForKey(2, Key.Right, modifiers));
    }

    [AvaloniaFact]
    public void ApplyKey_ChangesValueAndConsumesCommand()
    {
        var control = new StarRatingControl { Value = 2 };

        Assert.True(control.ApplyKey(Key.Right, KeyModifiers.None));
        Assert.Equal(3, control.Value);

        Assert.False(control.ApplyKey(Key.A, KeyModifiers.None));
        Assert.Equal(3, control.Value);
    }

    [AvaloniaFact]
    public void ApplyKey_IsIgnoredWhenDisabled()
    {
        var control = new StarRatingControl { Value = 2, IsEnabled = false };

        Assert.False(control.ApplyKey(Key.Right, KeyModifiers.None));
        Assert.Equal(2, control.Value);
    }

    [AvaloniaFact]
    public void Hover_PreviewsFillLevelWithoutChangingValue()
    {
        var control = new StarRatingControl { Value = 1 };

        control.ApplyHover(4);

        Assert.Equal(1, control.Value);
        Assert.Equal(4, control.FillLevel);
        Assert.True(control.Stars[3].IsFilled);
        Assert.False(control.Stars[4].IsFilled);

        control.ApplyHover(0);
        Assert.Equal(1, control.FillLevel);
    }

    [AvaloniaFact]
    public void ChoosingAStar_ClearsHoverPreview()
    {
        var control = new StarRatingControl { Value = 1 };
        control.ApplyHover(5);

        control.ApplyStarClick(2);

        Assert.Equal(2, control.Value);
        Assert.Equal(2, control.FillLevel);
    }

    [AvaloniaFact]
    public void FilledState_FollowsValue()
    {
        var control = new StarRatingControl();

        control.ApplyValue(3);

        Assert.Equal([true, true, true, false, false], control.Stars.Select(star => star.IsFilled));
    }

    [AvaloniaFact]
    public void Accessibility_ExposesLabelValueAndStarNames()
    {
        var control = new StarRatingControl { Value = 3 };

        Assert.Equal("Rating:", AutomationProperties.GetName(control));
        Assert.Equal("3 of 5 stars", AutomationProperties.GetHelpText(control));
        Assert.Equal("2 stars", AutomationProperties.GetName(control.Stars[1]));

        control.ApplyValue(0);

        Assert.Equal("Not rated", AutomationProperties.GetHelpText(control));
    }

    [AvaloniaFact]
    public void Accessibility_ExposesRangeValuePattern()
    {
        var control = new StarRatingControl { Value = 2 };
        var peer = new StarRatingAutomationPeer(control);

        Assert.Equal(AutomationControlType.Slider, peer.GetAutomationControlType());
        Assert.Equal("Rating:", peer.GetName());

        var range = Assert.IsAssignableFrom<IRangeValueProvider>(control);
        Assert.Equal(2, range.Value);
        Assert.Equal(0, range.Minimum);
        Assert.Equal(StarRatingControl.MaximumValue, range.Maximum);
        Assert.Equal(1, range.SmallChange);
        Assert.False(range.IsReadOnly);

        range.SetValue(4.4);
        Assert.Equal(4, range.Value);

        range.SetValue(9);
        Assert.Equal(StarRatingControl.MaximumValue, range.Value);
    }

    [AvaloniaFact]
    public void Accessibility_RangeValueCannotChangeADisabledControl()
    {
        var control = new StarRatingControl { Value = 2, IsEnabled = false };

        var range = Assert.IsAssignableFrom<IRangeValueProvider>(control);

        Assert.True(range.IsReadOnly);
        Assert.Throws<System.InvalidOperationException>(() => range.SetValue(5));
        Assert.Equal(2, control.Value);
    }

    [AvaloniaFact]
    public void KeyboardInput_ThroughWindow_UpdatesTheValue()
    {
        var control = new StarRatingControl { Value = 2 };
        var window = new Window { Content = control, Width = 200, Height = 60 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(control.Focus());
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            Assert.Equal(3, control.Value);

            window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
            Assert.Equal(0, control.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PointerClick_OnRenderedStar_SelectsAndClearsTheRating()
    {
        var control = new StarRatingControl { Value = 0 };
        var window = new Window { Content = control, Width = 200, Height = 60 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var fourthStar = control.Stars[3];
            var center = fourthStar.TranslatePoint(new Point(fourthStar.Bounds.Width / 2, fourthStar.Bounds.Height / 2), window);
            Assert.NotNull(center);
            window.MouseDown(center!.Value, MouseButton.Left);
            window.MouseUp(center.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(4, control.Value);

            window.MouseDown(center.Value, MouseButton.Left);
            window.MouseUp(center.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, control.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Defaults_FollowTheLocalizationCatalog()
    {
        var control = new StarRatingControl();

        Assert.Equal(SkyCD.UI.Localization.LocalizationService.Current.Get(SkyCD.UI.Localization.UiKeys.RatingName),
            control.RatingName);
    }
}
