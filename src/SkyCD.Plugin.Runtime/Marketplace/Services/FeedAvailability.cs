namespace SkyCD.Plugin.Runtime.Marketplace.Services;

/// <summary>Whether one package version is listed by its feed.</summary>
public enum FeedAvailabilityState
{
    /// <summary>The feed lists the version.</summary>
    Available,

    /// <summary>The feed was read and does not list the version.</summary>
    Missing,

    /// <summary>The feed could not be read or exposes no package content resource.</summary>
    Failed
}

/// <summary>Availability of one version plus the explanation a report shows.</summary>
/// <param name="State">Availability state.</param>
/// <param name="Reason">Explanation for users.</param>
public sealed record FeedAvailability(FeedAvailabilityState State, string Reason)
{
    public static FeedAvailability Available { get; } = new(FeedAvailabilityState.Available, string.Empty);

    public static FeedAvailability Missing(string reason)
    {
        return new FeedAvailability(FeedAvailabilityState.Missing, reason);
    }

    public static FeedAvailability Failed(string reason)
    {
        return new FeedAvailability(FeedAvailabilityState.Failed, reason);
    }
}
