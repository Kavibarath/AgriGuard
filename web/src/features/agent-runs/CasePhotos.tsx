import { useEffect, useMemo, useState } from 'react'
import { Modal } from '@/components/ui/Modal'
import { Spinner } from '@/components/ui/Spinner'
import { useCasePhoto } from './queries'
import type { CasePhoto } from './types'

/** The farmer's leaf photos (§7 "photo viewer"): thumbnails, enlarged on click. */
export function CasePhotos({ caseId, photos }: { caseId: string; photos: CasePhoto[] }) {
  const [open, setOpen] = useState<{ url: string; index: number } | null>(null)

  if (photos.length === 0) return null

  return (
    <div>
      <h3 className="text-xs font-medium uppercase tracking-wide text-stone-500">Photos</h3>
      <ul className="mt-1.5 flex flex-wrap gap-2">
        {photos.map((photo, index) => (
          <li key={photo.id}>
            <Thumb caseId={caseId} photo={photo} index={index} onOpen={(url) => setOpen({ url, index })} />
          </li>
        ))}
      </ul>
      <Modal open={open !== null} onClose={() => setOpen(null)} title={`Photo ${(open?.index ?? 0) + 1} of ${photos.length}`}>
        {open && <img src={open.url} alt={`Photo ${open.index + 1} from the farmer`} className="max-h-[70vh] w-full rounded object-contain" />}
      </Modal>
    </div>
  )
}

function Thumb({ caseId, photo, index, onOpen }: { caseId: string; photo: CasePhoto; index: number; onOpen: (url: string) => void }) {
  const blob = useCasePhoto(caseId, photo.id)
  const url = useObjectUrl(blob.data)

  if (blob.isPending) {
    return (
      <div className="flex size-24 items-center justify-center rounded bg-stone-100">
        <Spinner label={`Loading photo ${index + 1}`} />
      </div>
    )
  }

  if (blob.error || !url) {
    return <div className="flex size-24 items-center justify-center rounded bg-stone-100 text-xs text-stone-500">Could not load</div>
  }

  return (
    <button
      type="button"
      onClick={() => onOpen(url)}
      className="block overflow-hidden rounded focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500"
    >
      <img src={url} alt={`Photo ${index + 1} from the farmer — open larger`} className="size-24 object-cover" />
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
