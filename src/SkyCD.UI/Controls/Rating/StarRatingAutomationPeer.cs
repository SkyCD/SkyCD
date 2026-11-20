using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace SkyCD.UI.Controls.Rating;

/// <summary>
/// Exposes <see cref="StarRatingControl"/> to assistive technology as a range value: screen readers read the
/// current rating and can set a new one, and every change is submitted like a pointer or keyboard change.
/// </summary>
public sealed class StarRatingAutomationPeer(StarRatingControl owner) : ControlAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

    protected override string GetClassNameCore() => nameof(StarRatingControl);

    protected override string? GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrWhiteSpace(name) ? ((StarRatingControl)Owner).RatingName : name;
    }

    protected override string? GetHelpTextCore() => AutomationProperties.GetHelpText(Owner);
}
