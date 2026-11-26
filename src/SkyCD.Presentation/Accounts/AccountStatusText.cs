using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Accounts;

/// <summary>Localized label for the status codes produced by <c>EvaluateStatus</c>.</summary>
public static class AccountStatusText
{
    public static string Get(string? status) => status switch
    {
        "active" => LocalizationService.Current.Get(UiKeys.AccountStatusActive),
        "expiring" => LocalizationService.Current.Get(UiKeys.AccountStatusExpiring),
        "expired" => LocalizationService.Current.Get(UiKeys.AccountStatusExpired),
        "suspended" => LocalizationService.Current.Get(UiKeys.AccountStatusSuspended),
        "no-expiry" => LocalizationService.Current.Get(UiKeys.AccountStatusNoExpiry),
        "no-renewal" => LocalizationService.Current.Get(UiKeys.AccountStatusNoRenewal),
        _ => string.IsNullOrWhiteSpace(status)
            ? LocalizationService.Current.Get(UiKeys.AccountStatusUnknown)
            : status
    };
}
