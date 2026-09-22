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
}: {
  columns: Column<T>[]
  rows: T[]
  rowKey: (row: T) => string
  sort?: SortState
  onSortChange?: (next: SortState) => void
  onRowClick?: (row: T) => void
  caption: string
}) {
  const toggleSort = (key: string) => {
    if (!onSortChange) return
    onSortChange(sort?.sortBy === key ? { sortBy: key, desc: !sort.desc } : { sortBy: key, desc: false })
  }

  return (
    <div className="overflow-x-auto rounded-lg border border-stone-200 bg-white">
      <table className="w-full text-sm">
        <caption className="sr-only">{caption}</caption>
        <thead className="border-b border-stone-200 bg-stone-50 text-left text-xs uppercase tracking-wide text-stone-600">
          <tr>
            {columns.map((column) => (
              <th
                key={column.key}
                scope="col"
                className={cn('px-3 py-2 font-medium', column.numeric && 'text-right', column.secondary && 'hidden sm:table-cell')}
                // Announces the current sort to screen readers, not just the arrow glyph.
                aria-sort={
                  sort?.sortBy === column.key ? (sort.desc ? 'descending' : 'ascending') : undefined
                }
              >
                {column.sortable && onSortChange ? (
                  <button
                    type="button"
                    onClick={() => toggleSort(column.key)}
                    className="inline-flex items-center gap-1 hover:text-stone-900 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500"
                  >
                    {column.header}
                    <span aria-hidden="true" className="text-stone-400">
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
        <tbody className="divide-y divide-stone-100">
          {rows.map((row) => (
            <tr
              key={rowKey(row)}
              onClick={onRowClick ? () => onRowClick(row) : undefined}
              className={cn(onRowClick && 'cursor-pointer hover:bg-stone-50')}
            >
              {columns.map((column) => (
                <td
                  key={column.key}
                  className={cn('px-3 py-2 align-middle', column.numeric && 'text-right tabular-nums', column.secondary && 'hidden sm:table-cell')}
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
        <Button variant="secondary" className="h-8" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          Previous
        </Button>
        <Button variant="secondary" className="h-8" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>
          Next
        </Button>
      </div>
    </nav>
  )
}
