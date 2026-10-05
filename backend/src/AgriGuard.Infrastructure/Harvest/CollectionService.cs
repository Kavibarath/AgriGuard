using System.Linq.Expressions;
using AgriGuard.Application.Common;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Application.Common.Models;
using AgriGuard.Application.Harvest;
using AgriGuard.Domain.Harvest;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Registry;
using AgriGuard.Infrastructure.Persistence;
using AgriGuard.Infrastructure.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgriGuard.Infrastructure.Harvest;

/// <summary>
/// Collection centres, their time slots, and farmers' bookings into them.
///
/// **Allocation** (POST /api/collection-bookings/allocate, §5.1 non-CRUD) is one serializable
/// transaction: the candidate slots are locked with SELECT … FOR UPDATE, <see cref="SlotAllocation"/>
/// picks the first with room for the whole harvest (preferred day first, nearest centre next), and
/// the slot's BookedKg grows by exactly the booking. Two farmers racing for the last room queue on
/// the lock; the second sees it taken and is offered the next slot, or refused. The CHECK
/// booked_kg &lt;= capacity_kg is the backstop.
///
/// A booking is refused before the crop is safe to harvest: every spray's pre-harvest interval
/// (from the rules table) must have passed by the collection day.
/// </summary>
public sealed class CollectionService(
    AgriGuardDbContext db,
    ICurrentUserAccessor currentUser,
    FarmCalendar calendar,
    ILogger<CollectionService> logger) : ICollectionService
{
    private const string RaceMessage = "Someone else booked the same slot at the same moment. Try again.";

    private static readonly Dictionary<string, Expression<Func<CollectionBooking, object>>> BookingSortable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["slotDate"] = b => b.Slot.SlotDate,
        ["createdAt"] = b => b.CreatedAt,
        ["quantityKg"] = b => b.QuantityKg
    };

    public async Task<IReadOnlyList<CollectionCentreDto>> ListCentresAsync(Guid? districtId, CancellationToken ct = default)
    {
        var centres = db.CollectionCentres.AsNoTracking().Where(c => c.IsActive);
        if (districtId is { } id)
            centres = centres.Where(c => c.DistrictId == id);
        return await centres
            .OrderBy(c => c.Name)
            .Select(c => new CollectionCentreDto(c.Id, c.Name, c.DistrictId, c.District.Name, c.Latitude, c.Longitude, c.DailyCapacityKg))
            .ToListAsync(ct);
    }

    public async Task<PagedResult<CollectionSlotDto>> ListSlotsAsync(CollectionSlotQuery query, CancellationToken ct = default)
    {
        var slots = db.CollectionSlots.AsNoTracking().Where(s => s.Centre.IsActive);
        if (query.Date is { } date)
            slots = slots.Where(s => s.SlotDate == date);
        if (query.From is { } from)
            slots = slots.Where(s => s.SlotDate >= from);
        if (query.To is { } to)
            slots = slots.Where(s => s.SlotDate <= to);
        if (query.CentreId is { } centreId)
            slots = slots.Where(s => s.CentreId == centreId);
        if (query.DistrictId is { } districtId)
            slots = slots.Where(s => s.Centre.DistrictId == districtId);
        if (query.MinRemainingKg is { } room)
            slots = slots.Where(s => s.CapacityKg - s.BookedKg >= room);

        return await slots
            .OrderBy(s => s.SlotDate).ThenBy(s => s.Centre.Name).ThenBy(s => s.SlotIndex).ThenBy(s => s.Id)
            .Select(SlotProjection)
            .ToPagedResultAsync(query, ct);
    }

    public async Task<CollectionSlotDto> CreateSlotAsync(CreateCollectionSlotRequest request, CancellationToken ct = default)
    {
        var centre = await db.CollectionCentres.AsNoTracking()
            .Where(c => c.Id == request.CentreId)
            .Select(c => new { c.Name, c.DailyCapacityKg, c.IsActive })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Collection centre", request.CentreId);

        if (!centre.IsActive)
            throw new BusinessRuleException("CENTRE_CLOSED", $"{centre.Name} is closed.");
        if (request.SlotDate < calendar.Today)
            throw new RequestValidationException(nameof(request.SlotDate), "A slot cannot be added in the past.");

        // A centre can weigh and store only so much a day: the day's slots together may not exceed it.
        var alreadyThatDay = await db.CollectionSlots
            .Where(s => s.CentreId == request.CentreId && s.SlotDate == request.SlotDate)
            .SumAsync(s => (decimal?)s.CapacityKg, ct) ?? 0m;
        if (alreadyThatDay + request.CapacityKg > centre.DailyCapacityKg)
            throw new BusinessRuleException("CENTRE_CAPACITY_EXCEEDED",
                $"{centre.Name} handles {centre.DailyCapacityKg:0} kg a day; {alreadyThatDay:0} kg of slots already exist on {request.SlotDate:yyyy-MM-dd}.");

        var slot = new CollectionSlot
        {
            CentreId = request.CentreId,
            SlotDate = request.SlotDate,
            SlotIndex = request.SlotIndex,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            CapacityKg = request.CapacityKg
        };
        db.CollectionSlots.Add(slot);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex))
        {
            throw new ConflictException($"{centre.Name} already has slot {request.SlotIndex} on {request.SlotDate:yyyy-MM-dd}.");
        }

        return await db.CollectionSlots.AsNoTracking().Where(s => s.Id == slot.Id).Select(SlotProjection).FirstAsync(ct);
    }

    public async Task<PagedResult<CollectionBookingDto>> ListBookingsAsync(CollectionBookingQuery query, CancellationToken ct = default)
    {
        var bookings = db.CollectionBookings.AsNoTracking().ScopedTo(currentUser);
        if (query.Status is { } status)
            bookings = bookings.Where(b => b.Status == status);
        if (query.From is { } from)
            bookings = bookings.Where(b => b.Slot.SlotDate >= from);
        if (query.CentreId is { } centreId)
            bookings = bookings.Where(b => b.Slot.CentreId == centreId);

        var page = await bookings
            .OrderByAllowed(query, BookingSortable, b => b.Slot.SlotDate, b => b.Id)
            .Select(BookingRow)
            .ToPagedResultAsync(query, ct);
        return new PagedResult<CollectionBookingDto>([.. page.Items.Select(ToDto)], page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<CollectionBookingDto> AllocateAsync(AllocateBookingRequest request, CancellationToken ct = default)
    {
        var cycle = await db.CropCycles.AsNoTracking().ScopedTo(currentUser)
            .Where(c => c.Id == request.CropCycleId)
            .Select(c => new
            {
                c.Id,
                c.CropId,
                c.Status,
                c.Plot.Latitude,
                c.Plot.Longitude,
                c.Plot.Farm.FarmerId,
                c.Plot.Farm.DistrictId
            })
            .FirstOrDefaultAsync(ct);
        RegistryScope.EnsureVisible(cycle, await db.CropCycles.AnyAsync(c => c.Id == request.CropCycleId, ct), "Crop cycle", request.CropCycleId);

        // The farmer books their own harvest; an administrator may book on their behalf.
        if (currentUser.Role != UserRole.CoopAdministrator && cycle!.FarmerId != currentUser.UserId)
            throw new ForbiddenAccessException("Only the farmer who grows this crop can book its collection.");
        if (cycle!.Status != CropCycleStatus.Active)
            throw new BusinessRuleException("CYCLE_NOT_ACTIVE", "This crop has already been harvested or abandoned.");
        if (request.PreferredDate < calendar.Today)
            throw new RequestValidationException(nameof(request.PreferredDate), "The collection date cannot be in the past.");

        // Chemical safety first: the produce must be safe to eat on the day it is collected.
        var sprays = await HarvestScope.SpraysAsync(db, cycle.Id, cycle.CropId, ct);
        if (sprays.MaxBy(s => s.ClearsOn) is { } limiting && request.PreferredDate < limiting.ClearsOn)
            throw new BusinessRuleException("HARVEST_BEFORE_PHI",
                $"Not safe to harvest before {limiting.ClearsOn:yyyy-MM-dd}: {limiting.ProductName} was sprayed on " +
                $"{limiting.SprayedOn:yyyy-MM-dd} and needs {limiting.PreHarvestIntervalDays} days to clear.");

        var centres = await db.CollectionCentres.AsNoTracking()
            .Where(c => c.IsActive && (request.CentreId == null ? c.DistrictId == cycle.DistrictId : c.Id == request.CentreId))
            .Select(c => new { c.Id, c.Latitude, c.Longitude })
            .ToDictionaryAsync(c => c.Id, ct);
        if (centres.Count == 0)
            throw new BusinessRuleException("NO_CENTRE", "There is no open collection centre for this farm's district.");

        var centreIds = centres.Keys.ToArray();
        var lastDay = request.PreferredDate.AddDays(SlotAllocation.MaxDaysLater);
        var year = calendar.Today.Year;

        var booking = await SerializableTransaction.RunAsync(db, async () =>
        {
            // Lock every candidate slot (in id order, so racing bookings queue rather than deadlock).
            var slots = await db.CollectionSlots.FromSql($"""
                SELECT s.*, s.xmin FROM collection_slots s
                WHERE s.centre_id = ANY({centreIds})
                  AND s.slot_date BETWEEN {request.PreferredDate} AND {lastDay}
                  AND s.capacity_kg - s.booked_kg >= {request.QuantityKg}
                ORDER BY s.id
                FOR UPDATE OF s
                """).ToListAsync(ct);

            var choice = SlotAllocation.Choose(
                slots.Select(s => new SlotOption(s.Id, s.CentreId, s.SlotDate, s.SlotIndex, s.RemainingKg,
                    centres[s.CentreId].Latitude, centres[s.CentreId].Longitude)),
                request.QuantityKg, request.PreferredDate, cycle.Latitude, cycle.Longitude)
                ?? throw new BusinessRuleException("NO_CAPACITY",
                    $"No collection slot has room for {request.QuantityKg:0.##} kg between {request.PreferredDate:yyyy-MM-dd} and {lastDay:yyyy-MM-dd}. " +
                    "Try a later date, or split the harvest into smaller deliveries.");

            var slot = slots.Single(s => s.Id == choice.SlotId);
            slot.BookedKg += request.QuantityKg;

            var created = new CollectionBooking
            {
                BookingNo = await Sequences.NextAsync(db, AgriGuardDbContext.CollectionBookingSequence, "BK", year, ct),
                SlotId = slot.Id,
                CropCycleId = cycle.Id,
                FarmerId = cycle.FarmerId,
                QuantityKg = request.QuantityKg,
                Status = BookingStatus.Booked
            };
            db.CollectionBookings.Add(created);
            return created;
        }, RaceMessage, ct);

        logger.LogInformation("Booked {QuantityKg} kg of cycle {CropCycleId} into slot {SlotId} as {BookingNo}",
            request.QuantityKg, cycle.Id, booking.SlotId, booking.BookingNo);
        return await GetAsync(booking.Id, ct);
    }

    public async Task<CollectionBookingDto> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var visible = await db.CollectionBookings.AsNoTracking().ScopedTo(currentUser)
            .Where(b => b.Id == id)
            .Select(b => new { b.FarmerId })
            .FirstOrDefaultAsync(ct);
        RegistryScope.EnsureVisible(visible, await db.CollectionBookings.AnyAsync(b => b.Id == id, ct), "Booking", id);
        if (currentUser.Role != UserRole.CoopAdministrator && visible!.FarmerId != currentUser.UserId)
            throw new ForbiddenAccessException("Only the farmer who made the booking can cancel it.");

        var today = calendar.Today;
        await SerializableTransaction.RunAsync(db, async () =>
        {
            var booking = await db.CollectionBookings.FirstAsync(b => b.Id == id, ct);
            if (booking.Status != BookingStatus.Booked)
                throw new ConflictException($"This booking is already {booking.Status}.");

            var slot = await db.CollectionSlots
                .FromSql($"SELECT s.*, s.xmin FROM collection_slots s WHERE s.id = {booking.SlotId} FOR UPDATE")
                .FirstAsync(ct);
            if (slot.SlotDate < today)
                throw new BusinessRuleException("SLOT_PASSED", "The collection day has passed; this booking can no longer be cancelled.");

            booking.Status = BookingStatus.Cancelled;
            slot.BookedKg -= booking.QuantityKg;
            return booking;
        }, RaceMessage, ct);

        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Non-CRUD: co-op staff record the booking's day at the centre — check in, weigh and complete,
    /// or mark missed (<see cref="BookingStatusRules"/>). The request names the target status, so a
    /// repeated click changes nothing. Staff see only bookings in their scope (an agronomist, their
    /// district); a booking outside it is 403, as everywhere else.
    /// </summary>
    public async Task<CollectionBookingDto> RecordAsync(Guid id, RecordBookingRequest request, CancellationToken ct = default)
    {
        if (currentUser.Role is not (UserRole.FieldAgronomist or UserRole.CoopAdministrator))
            throw new ForbiddenAccessException("Only co-op staff at the collection centre record check-ins and weights.");

        var visible = await db.CollectionBookings.AsNoTracking().ScopedTo(currentUser).AnyAsync(b => b.Id == id, ct);
        RegistryScope.EnsureVisible(visible ? (object)true : null, await db.CollectionBookings.AnyAsync(b => b.Id == id, ct), "Booking", id);

        if (request.Status == BookingStatus.Completed && BookingStatusRules.ExplainWeight(request.ActualQuantityKg) is { } badWeight)
            throw new RequestValidationException(nameof(request.ActualQuantityKg), badWeight);

        var today = calendar.Today;
        var (from, changed) = await SerializableTransaction.RunAsync(db, async () =>
        {
            var booking = await db.CollectionBookings.Include(b => b.Slot).FirstAsync(b => b.Id == id, ct);
            var was = booking.Status;

            // Already there: a double click, or two staff at once. Nothing to do.
            if (was == request.Status)
                return (was, false);

            if (BookingStatusRules.ExplainRecord(was, request.Status, booking.Slot.SlotDate, today) is { } refusal)
                throw new BusinessRuleException("ILLEGAL_BOOKING_TRANSITION", refusal);

            booking.Status = request.Status;
            if (request.Status == BookingStatus.Completed)
                booking.ActualQuantityKg = request.ActualQuantityKg;
            return (was, true);
        }, "This booking was being updated at the same moment. Reload to see where it is.", ct);

        if (changed)
            logger.LogInformation("Booking {BookingId} {From} → {To} by {UserId}{Weight}", id, from, request.Status, currentUser.UserId,
                request.Status == BookingStatus.Completed ? $", {request.ActualQuantityKg} kg delivered" : "");
        return await GetAsync(id, ct);
    }

    private async Task<CollectionBookingDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.CollectionBookings.AsNoTracking().Where(b => b.Id == id).Select(BookingRow).FirstAsync(ct));

    private static Expression<Func<CollectionSlot, CollectionSlotDto>> SlotProjection => s => new CollectionSlotDto(
        s.Id, s.CentreId, s.Centre.Name, s.SlotDate, s.SlotIndex, s.StartTime, s.EndTime, s.CapacityKg, s.BookedKg, s.CapacityKg - s.BookedKg);

    /// <summary>What SQL returns; the distance is worked out in memory.</summary>
    private sealed record BookingRowData(
        Guid Id, string BookingNo, BookingStatus Status, Guid SlotId, string CentreName, DateOnly SlotDate, TimeOnly StartTime,
        TimeOnly EndTime, Guid CropCycleId, string PlotCode, string CropName, string FarmerName, decimal QuantityKg,
        decimal? ActualQuantityKg, decimal PlotLatitude, decimal PlotLongitude, decimal CentreLatitude, decimal CentreLongitude, DateTime CreatedAt);

    private static Expression<Func<CollectionBooking, BookingRowData>> BookingRow => b => new BookingRowData(
        b.Id, b.BookingNo, b.Status, b.SlotId, b.Slot.Centre.Name, b.Slot.SlotDate, b.Slot.StartTime, b.Slot.EndTime,
        b.CropCycleId, b.CropCycle.Plot.PlotCode, b.CropCycle.Crop.Name, b.Farmer.FullName, b.QuantityKg,
        b.ActualQuantityKg, b.CropCycle.Plot.Latitude, b.CropCycle.Plot.Longitude, b.Slot.Centre.Latitude, b.Slot.Centre.Longitude, b.CreatedAt);

    private static CollectionBookingDto ToDto(BookingRowData r) => new(
        r.Id, r.BookingNo, r.Status, r.SlotId, r.CentreName, r.SlotDate, r.StartTime, r.EndTime, r.CropCycleId,
        r.PlotCode, r.CropName, r.FarmerName, r.QuantityKg, r.ActualQuantityKg,
        Math.Round(SlotAllocation.DistanceKm(r.PlotLatitude, r.PlotLongitude, r.CentreLatitude, r.CentreLongitude), 1),
        r.CreatedAt);
}
