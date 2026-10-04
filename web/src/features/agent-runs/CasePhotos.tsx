import { useEffect, useMemo, useState } from 'react'
import { Modal } from '@/components/ui/Modal'
import { Spinner } from '@/components/ui/Spinner'
import { cn } from '@/lib/utils'
import { useCasePhoto } from './queries'
import type { CasePhoto } from './types'

/**
 * The farmer's leaf photos (§7 "photo viewer"): thumbnails, enlarged on click. With `feature`, the
 * first photo is shown large — on the case page the leaf is the evidence — and the rest as thumbnails.
 */
export function CasePhotos({ caseId, photos, feature = false }: { caseId: string; photos: CasePhoto[]; feature?: boolean }) {
  const [open, setOpen] = useState<{ url: string; index: number } | null>(null)

  if (photos.length === 0) return null

  return (
    <div>
      <h3 className="text-sm font-semibold text-stone-800">Photos</h3>
      <ul className={cn('mt-1.5 gap-2', feature ? 'grid grid-cols-3' : 'flex flex-wrap')}>
        {photos.map((photo, index) => (
          <li key={photo.id} className={cn(feature && index === 0 && 'col-span-3')}>
            <Thumb caseId={caseId} photo={photo} index={index} large={feature && index === 0} onOpen={(url) => setOpen({ url, index })} />
          </li>
        ))}
      </ul>
      <Modal open={open !== null} onClose={() => setOpen(null)} title={`Photo ${(open?.index ?? 0) + 1} of ${photos.length}`}>
        {open && <img src={open.url} alt={`Photo ${open.index + 1} from the farmer`} className="max-h-[70vh] w-full rounded-lg object-contain" />}
      </Modal>
    </div>
  )
}

function Thumb({
  caseId,
  photo,
  index,
  large,
  onOpen,
}: {
  caseId: string
  photo: CasePhoto
  index: number
  large: boolean
  onOpen: (url: string) => void
}) {
  const blob = useCasePhoto(caseId, photo.id)
  const url = useObjectUrl(blob.data)
  const box = large ? 'aspect-[4/3] w-full' : 'size-24'

  if (blob.isPending) {
    return (
      <div className={cn('flex items-center justify-center rounded-lg bg-surface-inset', box)}>
        <Spinner label={`Loading photo ${index + 1}`} />
      </div>
    )
  }

  if (blob.error || !url) {
    return <div className={cn('flex items-center justify-center rounded-lg bg-surface-inset text-xs text-stone-600', box)}>Could not load</div>
  }

  return (
    <button type="button" onClick={() => onOpen(url)} className={cn('block overflow-hidden rounded-lg ring-1 ring-border-subtle', large ? 'w-full' : '')}>
      <img src={url} alt={`Photo ${index + 1} from the farmer — open larger`} className={cn('object-cover', box)} />
    </button>
  )
}

/** An object URL for a fetched image, released when the image changes or is no longer shown. */
function useObjectUrl(blob: Blob | undefined): string | null {
  const url = useMemo(() => (blob ? URL.createObjectURL(blob) : null), [blob])
  useEffect(() => () => {
    if (url) URL.revokeObjectURL(url)
  }, [url])
  return url
}
