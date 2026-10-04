namespace AgriGuard.Domain.Inventory;

/// <summary>How close a batch is to its expiry date, for the dealer's warnings.</summary>
public enum ExpiryState { InDate, ExpiringSoon, Expired }

public static class BatchExpiry
{
    /// <summary>Within this many days a batch is flagged, so it is sold or returned in time.</summary>
    public const int WarningDays = 30;

    /// <summary>A batch is expired on its expiry date: it is sellable only while the date is still ahead.</summary>
    public static ExpiryState Classify(DateOnly expiryDate, DateOnly today) =>
        expiryDate <= today ? ExpiryState.Expired
        : expiryDate <= today.AddDays(WarningDays) ? ExpiryState.ExpiringSoon
        : ExpiryState.InDate;
}
