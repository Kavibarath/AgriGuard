import { useState } from 'react'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { Modal } from '@/components/ui/Modal'
import { SelectField } from '@/components/ui/select'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { stageTone } from '@/components/ui/status-tones'
import { ApiError, userMessage } from '@/lib/api'
import { useAdvanceStage, useCreateCropCycle, useCrops, useCropCycle } from './queries'
import { stageLabels, type CropCycleSummary, type Plot } from './types'

const today = () => new Date().toISOString().slice(0, 10)

/**
 * The crop cycle on a plot: sow it, watch the harvest estimate, and advance it one stage at a
 * time. The advance button offers only the stage the API would accept — `allowedNextStages`
 * comes from the same CropStageRules the server validates against, so the UI cannot invite a
 * move that would be refused.
 */
export function CropCyclePanel({ plot, canEdit }: { plot: Plot; canEdit: boolean }) {
  const [sowing, setSowing] = useState(false)
  const [advancing, setAdvancing] = useState(false)
  const cycle = useCropCycle(plot.activeCycle?.id ?? null)

  if (!plot.activeCycle) {
    return (
      <section className="rounded-lg border border-dashed border-stone-300 bg-white p-4">
        <h3 className="font-medium text-stone-900">No crop growing</h3>
        <p className="mt-1 text-sm text-stone-600">
          {plot.status === 'Active'
            ? 'Record what you sowed to start tracking stages and harvest dates.'
            : `This plot is ${plot.status.toLowerCase()} and cannot be sown.`}
        </p>
        {canEdit && plot.status === 'Active' && (
          <Button className="mt-3" onClick={() => setSowing(true)}>
            Record sowing
          </Button>
        )}
        <SowModal open={sowing} onClose={() => setSowing(false)} plot={plot} />
      </section>
    )
  }

  const summary: CropCycleSummary = plot.activeCycle
  const detail = cycle.data
  const nextStage = detail?.allowedNextStages[0]

  return (
    <section className="space-y-4 rounded-lg border border-stone-200 bg-white p-4">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="font-medium text-stone-900">{summary.cropName}</h3>
          <p className="text-sm text-stone-600">Sown {summary.sownDate}</p>
        </div>
        <StatusBadge label={stageLabels[summary.stage]} tone={stageTone[summary.stage] ?? 'neutral'} />
      </header>

      <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-3">
        <div>
          <dt className="text-stone-500">Expected harvest</dt>
          <dd className="font-medium text-stone-900">{summary.expectedHarvestDate}</dd>
        </div>
        {summary.plannedHarvestDate && (
          <div>
            <dt className="text-stone-500">Planned harvest</dt>
            <dd className="font-medium text-stone-900">{summary.plannedHarvestDate}</dd>
          </div>
        )}
        {detail && (
          <div>
            <dt className="text-stone-500">Days to harvest</dt>
            <dd className="font-medium text-stone-900">
              {detail.daysToHarvest >= 0 ? detail.daysToHarvest : `${Math.abs(detail.daysToHarvest)} overdue`}
            </dd>
          </div>
        )}
      </dl>

      {canEdit && nextStage && (
        <Button onClick={() => setAdvancing(true)}>Advance to {stageLabels[nextStage]}</Button>
      )}

      {detail && detail.transitions.length > 0 && (
        <div>
          <h4 className="text-xs font-medium uppercase tracking-wide text-stone-500">Stage history</h4>
          <ol className="mt-2 space-y-1.5 text-sm">
            {detail.transitions.map((transition) => (
              <li key={`${transition.toStage}-${transition.transitionedAt}`} className="flex flex-wrap gap-x-2 text-stone-700">
                <span className="tabular-nums text-stone-500">{transition.transitionedAt.slice(0, 10)}</span>
                <span>
                  {stageLabels[transition.fromStage]} → <strong className="font-medium">{stageLabels[transition.toStage]}</strong>
                </span>
                {transition.note && <span className="text-stone-500">“{transition.note}”</span>}
              </li>
            ))}
          </ol>
        </div>
      )}

      {detail && nextStage && (
        <AdvanceStageModal
          open={advancing}
          onClose={() => setAdvancing(false)}
          cycleId={detail.id}
          fromStage={stageLabels[detail.stage]}
          toStage={nextStage}
          sownDate={detail.sownDate}
        />
      )}
    </section>
  )
}

function SowModal({ open, onClose, plot }: { open: boolean; onClose: () => void; plot: Plot }) {
  const crops = useCrops()
  const sow = useCreateCropCycle()
  const [cropId, setCropId] = useState('')
  const [sownDate, setSownDate] = useState(today())
  const [plannedHarvestDate, setPlannedHarvestDate] = useState('')

  const chosenCrop = crops.data?.find((crop) => crop.id === cropId)

  const close = () => {
    sow.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      onClose={close}
      title={`Record sowing on ${plot.plotCode}`}
      description="The expected harvest date is calculated from the crop's maturity period."
    >
      <form
        className="space-y-4"
        onSubmit={async (event) => {
          event.preventDefault()
          try {
            await sow.mutateAsync({
              plotId: plot.id,
              cropId,
              sownDate,
              plannedHarvestDate: plannedHarvestDate || null,
            })
            close()
          } catch (error) {
            if (!(error instanceof ApiError)) throw error
          }
        }}
      >
        {sow.error && <Alert tone="error">{userMessage(sow.error)}</Alert>}

        <SelectField label="Crop" required value={cropId} onChange={(event) => setCropId(event.target.value)}>
          <option value="">Choose a crop…</option>
          {crops.data?.map((crop) => (
            <option key={crop.id} value={crop.id}>
              {crop.name} ({crop.maturityDays} days)
            </option>
          ))}
        </SelectField>

        <Field
          label="Sowing date"
          type="date"
          required
          max={today()}
          value={sownDate}
          onChange={(event) => setSownDate(event.target.value)}
          hint={chosenCrop ? `Harvest expected around ${addDays(sownDate, chosenCrop.maturityDays)}` : undefined}
        />

        <Field
          label="Planned harvest date"
          type="date"
          hint="Optional — used for the pre-harvest interval check"
          value={plannedHarvestDate}
          onChange={(event) => setPlannedHarvestDate(event.target.value)}
        />

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" loading={sow.isPending} disabled={!cropId}>
            Record sowing
          </Button>
        </div>
      </form>
    </Modal>
  )
}

function AdvanceStageModal({
  open,
  onClose,
  cycleId,
  fromStage,
  toStage,
  sownDate,
}: {
  open: boolean
  onClose: () => void
  cycleId: string
  fromStage: string
  toStage: keyof typeof stageLabels
  sownDate: string
}) {
  const advance = useAdvanceStage()
  const [reachedOn, setReachedOn] = useState(today())
  const [note, setNote] = useState('')

  const close = () => {
    advance.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      onClose={close}
      title={`Advance to ${stageLabels[toStage]}`}
      description={`Moving from ${fromStage}. The harvest estimate is revised from the date you give.`}
    >
      <form
        className="space-y-4"
        onSubmit={async (event) => {
          event.preventDefault()
          try {
            await advance.mutateAsync({ id: cycleId, toStage, reachedOn, note: note || null })
            close()
          } catch (error) {
            // 422 explains an illegal transition, 400 an impossible date; both are shown below.
            if (!(error instanceof ApiError)) throw error
          }
        }}
      >
        {advance.error && <Alert tone="error">{userMessage(advance.error)}</Alert>}

        <Field
          label="Date reached"
          type="date"
          required
          min={sownDate}
          max={today()}
          value={reachedOn}
          onChange={(event) => setReachedOn(event.target.value)}
        />

        <Field
          label="Note"
          hint="Optional — what you observed"
          maxLength={500}
          value={note}
          onChange={(event) => setNote(event.target.value)}
        />

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" loading={advance.isPending}>
            Confirm {stageLabels[toStage]}
          </Button>
        </div>
      </form>
    </Modal>
  )
}

/** Local preview of the server's calculation, so the farmer sees the consequence before saving. */
function addDays(isoDate: string, days: number): string {
  const date = new Date(isoDate)
  date.setDate(date.getDate() + days)
  return date.toISOString().slice(0, 10)
}
