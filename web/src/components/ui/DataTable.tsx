import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'
import { Button } from './button'

export interface Column<T> {
  /** Stable key; when `sortable`, it is also the API's `sortBy` value, so the allow-list matches. */
  key: string
  header: string
  render: (row: T) => ReactNode
  sortable?: boolean
  /** Right-align numbers so magnitudes line up. */
  numeric?: boolean
  /** Hidden below `sm` — keeps the table readable on a phone without a horizontal scroll. */
  secondary?: boolean
}

export interface SortState {
  sortBy?: string
  desc: boolean
}

/**
 * Server-driven table: sorting and paging are requests, not client-side array operations, so a
 * farm with 500 plots still renders one page. Shared by all four components (§7).
 */
export function DataTable<T>({
  columns,
  rows,
  rowKey,
  sort,
  onSortChange,
  onRowClick,
  caption,
  tone = 'canopy',
}: {
  columns: Column<T>[]
  rows: T[]
  rowKey: (row: T) => string
  sort?: SortState
  onSortChange?: (next: SortState) => void
  onRowClick?: (row: T) => void
  caption: string
  /** `earth` for the dealer's stock and orders: soil-toned header and stripes. */
  tone?: 'canopy' | 'earth'
}) {
  const toggleSort = (key: string) => {
    if (!onSortChange) return
    onSortChange(sort?.sortBy === key ? { sortBy: key, desc: !sort.desc } : { sortBy: key, desc: false })
  }

  return (
    // Scrolls sideways on narrow screens; on wide ones it does not clip, so the header can stick
    // under the top bar while the page scrolls.
    <div className="card-raised overflow-x-auto rounded-xl border border-border-subtle bg-surface-card lg:overflow-x-visible">
      <table className="w-full border-separate border-spacing-0 text-sm">
        <caption className="sr-only">{caption}</caption>
        <thead className="text-left text-xs text-stone-600">
          <tr>
            {columns.map((column) => (
              <th
                key={column.key}
                scope="col"
                className={cn(
                  'sticky top-14 z-10 h-10 border-b border-border-subtle px-3 font-semibold whitespace-nowrap first:rounded-tl-xl last:rounded-tr-xl',
                  tone === 'earth' ? 'bg-earth-50 text-earth-800' : 'bg-surface-sunken',
                  column.numeric && 'text-right',
                  column.secondary && 'hidden sm:table-cell',
                )}
                // Announces the current sort to screen readers, not just the arrow glyph.
                aria-sort={
                  sort?.sortBy === column.key ? (sort.desc ? 'descending' : 'ascending') : undefined
                }
              >
                {column.sortable && onSortChange ? (
                  <button
                    type="button"
                    onClick={() => toggleSort(column.key)}
                    className="inline-flex items-center gap-1 rounded-sm hover:text-stone-900"
                  >
                    {column.header}
                    <span aria-hidden="true" className={sort?.sortBy === column.key ? 'text-brand-700' : 'text-stone-400'}>
                      {sort?.sortBy === column.key ? (sort.desc ? '▾' : '▴') : '↕'}
                    </span>
                  </button>
                ) : (
                  column.header
                )}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="[&>tr:last-child>td]:border-b-0">
          {rows.map((row) => (
            <tr
              key={rowKey(row)}
              onClick={onRowClick ? () => onRowClick(row) : undefined}
              className={cn(tone === 'earth' ? 'even:bg-earth-50/60' : 'even:bg-surface-sunken/70', onRowClick && 'cursor-pointer hover:bg-brand-50')}
            >
              {columns.map((column) => (
                <td
                  key={column.key}
                  className={cn(
                    'h-11 border-b border-border-subtle px-3 py-1.5 align-middle',
                    column.numeric && 'text-right whitespace-nowrap tabular-nums',
                    column.secondary && 'hidden sm:table-cell',
                  )}
                >
                  {column.render(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/** Page controls for a PagedResult. Hidden entirely when everything fits on one page. */
export function Pagination({
  page,
  totalPages,
  totalCount,
  onPageChange,
}: {
  page: number
  totalPages: number
  totalCount: number
  onPageChange: (page: number) => void
}) {
  if (totalPages <= 1) return null

  return (
    <nav className="flex items-center justify-between gap-4 text-sm" aria-label="Pagination">
      <p className="text-stone-600">
        Page {page} of {totalPages} · {totalCount} total
      </p>
      <div className="flex gap-2">
        <Button variant="secondary" size="sm" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          Previous
        </Button>
        <Button variant="secondary" size="sm" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>
          Next
        </Button>
      </div>
    </nav>
  )
}
