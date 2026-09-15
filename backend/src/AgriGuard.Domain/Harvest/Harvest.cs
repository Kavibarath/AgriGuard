// Component D — Harvest Windows, Collection Logistics & Regional Intelligence
using AgriGuard.Domain.Common;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Reference;
using AgriGuard.Domain.Registry;

namespace AgriGuard.Domain.Harvest;

public enum ForecastSource { Farmer, Agronomist, Computed }

public enum BookingStatus { Booked, CheckedIn, Completed, Cancelled, NoShow }

public class HarvestForecast : AuditableEntity
{
    public Guid CropCycleId { get; set; }
    public CropCycle CropCycle { get; set; } = null!;

    public DateOnly ForecastHarvestDate { get; set; }
    public decimal EstimatedYieldKg { get; set; }
    public ForecastSource Source { get; set; }
    public string? Notes { get; set; }

    /// <summary>Filled when the harvest happens; powers the forecast-vs-actual report.</summary>
    public decimal? ActualYieldKg { get; set; }
}

public class CollectionCentre : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public Guid DistrictId { get; set; }
    public District District { get; set; } = null!;

    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public decimal DailyCapacityKg { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<CollectionSlot> Slots { get; set; } = new List<CollectionSlot>();
}

public class CollectionSlot : AuditableEntity
{
    public Guid CentreId { get; set; }
    public CollectionCentre Centre { get; set; } = null!;

    public DateOnly SlotDate { get; set; }

    /// <summary>Position within the day; (Centre, Date, Index) is unique.</summary>
    public int SlotIndex { get; set; }

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public decimal CapacityKg { get; set; }
    public decimal BookedKg { get; set; }

    public uint Version { get; set; }

    public decimal RemainingKg => CapacityKg - BookedKg;

    public ICollection<CollectionBooking> Bookings { get; set; } = new List<CollectionBooking>();
}

public class CollectionBooking : AuditableEntity
{
    public string BookingNo { get; set; } = string.Empty;

    public Guid SlotId { get; set; }
    public CollectionSlot Slot { get; set; } = null!;

    public Guid CropCycleId { get; set; }
    public CropCycle CropCycle { get; set; } = null!;

    public Guid FarmerId { get; set; }
    public User Farmer { get; set; } = null!;

    public decimal QuantityKg { get; set; }
    public decimal? ActualQuantityKg { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Booked;
}

/// <summary>
/// Cached Open-Meteo response keyed on coordinates rounded to 0.1° (~11 km) — coarse enough
/// to share across nearby plots and to avoid sending precise farm locations to a third party.
/// </summary>
public class WeatherSnapshot : Entity
{
    public decimal LatitudeRounded { get; set; }
    public decimal LongitudeRounded { get; set; }
    public DateTime FetchedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Normalised forecast (jsonb), not the raw provider payload.</summary>
    public string ForecastJson { get; set; } = string.Empty;
}
