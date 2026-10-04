import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { FilterBar } from '@/components/layout/FilterBar'
import { PageHeader } from '@/components/layout/PageHeader'
import { Basket, Truck, XCircle } from '@/components/icons'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { Button } from '@/components/ui/button'
import { DataTable, Pagination, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/EmptyState'
import { SelectField } from '@/components/ui/select'
import { StatTile } from '@/components/ui/StatTile'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { bookingStatusTone } from '@/components/ui/status-tones'
import { useCurrentUser } from '@/features/auth/auth-store'
import { can } from '@/features/auth/policies'
import { isoToday } from '@/features/inventory/format'
import { useDistricts } from '@/features/registry/queries'
import { addDays, weekdayDay } from '@/lib/dates'
import { cn } from '@/lib/utils'
import { formatKg } from './format'
import { useBookings, useCentres, useSlots } from './queries'
import { SlotFormModal } from './SlotFormModal'
import type { CollectionBooking, CollectionSlot } from './types'

const DAYS = 7
/** The API's page cap. A week of every centre fits (4 centres × 7 days × 3 slots = 84). */
const MAX_SLOTS = 100

const hhmm = (time: string) => time.slice(0, 5)

/**
 * How full a slot is, as a bar and in words. A full slot is filled edge to edge in red, marked
 * with a cross and the word "Full": the allocator refuses more, and the planner shows it cannot.
 */
function SlotMeter({ slot }: { slot: CollectionSlot }) {
  const share = slot.capacityKg > 0 ? Math.min(1, slot.bookedKg / slot.capacityKg) : 1
  const full = slot.remainingKg <= 0
  const tight = !full && share >= 0.8
  return (
    <li className="space-y-1" title={`${hhmm(slot.startTime)}–${hhmm(slot.endTime)}: ${formatKg(slot.bookedKg)} of ${formatKg(slot.capacityKg)} booked`}>
      <p className="flex justify-between gap-2 text-xs whitespace-nowrap">
        <span className="font-semibold text-stone-800 tabular-nums">{hhmm(slot.startTime)}</span>
        <span className={cn('inline-flex items-center gap-0.5 tabular-nums', full ? 'font-semibold text-danger-800' : tight ? 'font-medium text-warning-800' : 'text-stone-600')}>
          {full && <XCircle size={12} className="text-danger" />}
          {full ? 'Full' : `${formatKg(slot.remainingKg)} left`}
        </span>
      </p>
      <div
        className={cn('h-2 overflow-hidden rounded-full', full ? 'bg-danger-200' : 'bg-surface-inset')}
        role="meter"
        aria-valuemin={0}
        aria-valuemax={slot.capacityKg}
        aria-valuenow={slot.bookedKg}
        aria-label={`${hhmm(slot.startTime)}–${hhmm(slot.endTime)} slot: ${formatKg(slot.bookedKg)} of ${formatKg(slot.capacityKg)} booked`}
      >
        <div
          className={cn('h-full rounded-full', full ? 'bg-danger' : tight ? 'bg-warning' : 'bg-brand-500')}
          style={{ width: `${share * 100}%` }}
        />
      </div>
    </li>
  )
}

/** The week's capacity in four figures, from the slots on screen. */
function WeekTiles({ slots }: { slots: CollectionSlot[] | undefined }) {
  const capacity = slots?.reduce((sum, s) => sum + s.capacityKg, 0)
  const booked = slots?.reduce((sum, s) => sum + s.bookedKg, 0)
  const full = slots?.filter((s) => s.remainingKg <= 0).length
  const used = capacity ? Math.round(((booked ?? 0) / capacity) * 100) : undefined
  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <StatTile label="Capacity this week" value={capacity !== undefined ? formatKg(capacity) : '—'} detail={slots ? `${slots.length} slots open` : 'Reading the slots…'} icon={<Truck />} />
      <StatTile label="Booked" value={booked !== undefined ? formatKg(booked) : '—'} detail={used !== undefined ? `${used}% of capacity` : 'Of the week’s capacity'} icon={<Basket className="text-earth-600" />} />
      <StatTile label="Room left" value={capacity !== undefined && booked !== undefined ? formatKg(capacity - booked) : '—'} detail="Still bookable from the phone" />
      <StatTile label="Full slots" value={full ?? '—'} detail="Refuse any further booking" icon={<XCircle className="text-danger" />} />
    </div>
  )
}

/**
 * The collection planner (§7 /collection-planner, Component D): each centre's slots for a week,
 * with what is booked and what is left, and the bookings behind them. The co-op administrator
 * opens extra slots here. District, centre and week live in the URL.
 */
export function CollectionPlannerPage() {
  const user = useCurrentUser()
  const [params, setParams] = useSearchParams()
  const [opening, setOpening] = useState(false)
  const districts = useDistricts()

  const districtId = params.get('districtId') ?? undefined
  const centreId = params.get('centreId') ?? undefined
  const week = /^\d{4}-\d{2}-\d{2}$/.test(params.get('week') ?? '') ? params.get('week')! : isoToday()
  const lastDay = addDays(week, DAYS - 1)
  const days = Array.from({ length: DAYS }, (_, i) => addDays(week, i))

  const centres = useCentres(districtId)
  const slots = useSlots({ from: week, to: lastDay, districtId, centreId, pageSize: MAX_SLOTS })
  const bookingsQuery = { from: week, centreId, page: Number(params.get('page') ?? 1), pageSize: 10 }
  const bookings = useBookings(bookingsQuery)
  const canOpenSlots = can(user?.role, 'AdministersRules')

  const updateParams = (patch: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(patch)) {
      if (value) next.set(key, value)
      else next.delete(key)
    }
    if (!('page' in patch)) next.delete('page')
    setParams(next, { replace: true })
  }

  const shownCentres = (centres.data ?? []).filter((c) => !centreId || c.id === centreId)
  const cell = (centre: string, day: string) =>
    (slots.data?.items ?? []).filter((s) => s.centreId === centre && s.slotDate === day).sort((a, b) => a.slotIndex - b.slotIndex)

  const bookingColumns: Column<CollectionBooking>[] = [
    { key: 'bookingNo', header: 'Booking', render: (b) => <span className="font-medium text-stone-900">{b.bookingNo}</span> },
    {
      key: 'slot',
      header: 'When · where',
      render: (b) => (
        <div>
          <p className="tabular-nums">
            {weekdayDay(b.slotDate)}, {hhmm(b.startTime)}
          </p>
          <p className="text-xs text-stone-600">{b.centreName}</p>
        </div>
      ),
    },
    {
      key: 'farmer',
      header: 'Farmer · crop',
      render: (b) => (
        <div>
          <p>{b.farmerName}</p>
          <p className="text-xs text-stone-600">
            {b.cropName} · {b.plotCode} · {b.distanceKm} km away
          </p>
        </div>
      ),
    },
    { key: 'quantityKg', header: 'Quantity', numeric: true, render: (b) => formatKg(b.quantityKg) },
    { key: 'status', header: 'Status', render: (b) => <StatusBadge label={b.status} tone={bookingStatusTone[b.status]} /> },
  ]

  return (
    <div className="space-y-4 pb-4">
      <PageHeader
        title="Collection planner"
        description="Room left in each centre's slots, and the harvests booked into them."
        actions={canOpenSlots ? <Button onClick={() => setOpening(true)}>Open a slot</Button> : undefined}
      />

      <FilterBar className="sm:grid-cols-[14rem_18rem_1fr]">
        <SelectField label="District" value={districtId ?? ''} onChange={(e) => updateParams({ districtId: e.target.value || undefined, centreId: undefined })}>
          <option value="">All districts</option>
          {districts.data?.map((d) => (
            <option key={d.id} value={d.id}>
              {d.name}
            </option>
          ))}
        </SelectField>
        <SelectField label="Centre" value={centreId ?? ''} onChange={(e) => updateParams({ centreId: e.target.value || undefined })}>
          <option value="">All centres</option>
          {centres.data?.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </SelectField>
        <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Week">
          <Button variant="secondary" onClick={() => updateParams({ week: addDays(week, -DAYS) })}>
            ← Previous week
          </Button>
          <Button variant="secondary" onClick={() => updateParams({ week: undefined })}>
            From today
          </Button>
          <Button variant="secondary" onClick={() => updateParams({ week: addDays(week, DAYS) })}>
            Next week →
          </Button>
        </div>
      </FilterBar>

      <WeekTiles slots={slots.data?.items} />

      <section aria-labelledby="slots-heading" className="space-y-3">
        <div>
          <h2 id="slots-heading" className="font-display text-xl font-semibold text-stone-900">
            {weekdayDay(week)} – {weekdayDay(lastDay)}
          </h2>
          <p className="text-sm text-stone-600">Each day: booked of the day's capacity, then every slot's start time, the room left, and a bar of what is booked.</p>
        </div>
        <AsyncBoundary
          isPending={slots.isPending || centres.isPending}
          error={slots.error ?? centres.error}
          onRetry={() => void Promise.all([slots.refetch(), centres.refetch()])}
          label="Loading slots"
        >
          {shownCentres.length === 0 ? (
            <EmptyState title="No collection centre here" description="Choose another district." />
          ) : (
            <div className="space-y-2">
              {(slots.data?.totalCount ?? 0) > MAX_SLOTS && (
                <p className="text-sm font-medium text-warning-800">Showing the first {MAX_SLOTS} slots of the week. Choose a district or a centre to see them all.</p>
              )}
              <div className="card-raised overflow-x-auto rounded-xl border border-border-subtle bg-surface-card">
                <table className="w-full min-w-[64rem] table-fixed text-sm">
                  <caption className="sr-only">Collection slots by centre and day</caption>
                  <thead className="border-b border-border-subtle bg-surface-sunken text-left text-xs text-stone-600">
                    <tr>
                      <th scope="col" className="w-48 px-3 py-2 font-semibold">
                        Centre
                      </th>
                      {days.map((d) => (
                        <th key={d} scope="col" className="px-2 py-2 font-semibold">
                          {weekdayDay(d)}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border-subtle">
                    {shownCentres.map((centre) => (
                      <tr key={centre.id}>
                        <th scope="row" className="px-3 py-3 text-left align-top font-normal">
                          <p className="font-medium text-stone-900">{centre.name}</p>
                          <p className="text-xs text-stone-600">
                            {centre.districtName} · {formatKg(centre.dailyCapacityKg)} a day
                          </p>
                        </th>
                        {days.map((day) => {
                          const daySlots = cell(centre.id, day)
                          const booked = daySlots.reduce((sum, s) => sum + s.bookedKg, 0)
                          const capacity = daySlots.reduce((sum, s) => sum + s.capacityKg, 0)
                          return (
                            <td key={day} className="px-2 py-3 align-top">
                              {daySlots.length === 0 ? (
                                <p className="text-xs text-stone-500">No slots</p>
                              ) : (
                                <div className="space-y-2">
                                  <p className="whitespace-nowrap text-xs text-stone-600 tabular-nums">
                                    {formatKg(booked)} / {formatKg(capacity)}
                                  </p>
                                  <ul className="space-y-2">
                                    {daySlots.map((s) => (
                                      <SlotMeter key={s.id} slot={s} />
                                    ))}
                                  </ul>
                                </div>
                              )}
                            </td>
                          )
                        })}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </AsyncBoundary>
      </section>

      <section aria-labelledby="bookings-heading" className="space-y-3">
        <h2 id="bookings-heading" className="font-display text-xl font-semibold text-stone-900">
          Bookings from {weekdayDay(week)}
        </h2>
        <AsyncBoundary isPending={bookings.isPending} error={bookings.error} onRetry={bookings.refetch} label="Loading bookings">
          {bookings.data?.items.length === 0 ? (
            <EmptyState title="No bookings yet" description="Farmers book a slot from the harvest screen of the phone app." />
          ) : (
            <div className="space-y-3">
              <DataTable tone="earth" caption="Collection bookings" columns={bookingColumns} rows={bookings.data?.items ?? []} rowKey={(b) => b.id} />
              <Pagination
                page={bookings.data?.page ?? 1}
                totalPages={bookings.data?.totalPages ?? 1}
                totalCount={bookings.data?.totalCount ?? 0}
                onPageChange={(page) => updateParams({ page: String(page) })}
              />
            </div>
          )}
        </AsyncBoundary>
      </section>

      {canOpenSlots && (
        <SlotFormModal
          open={opening}
          onClose={() => setOpening(false)}
          centres={centres.data ?? []}
          defaults={{ centreId, slotDate: week < isoToday() ? isoToday() : week }}
        />
      )}
    </div>
  )
}
