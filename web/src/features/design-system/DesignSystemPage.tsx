import { useState, type ReactNode } from 'react'
import * as Icons from '@/components/icons'
import { BrandMark } from '@/components/layout/BrandMark'
import { PageHeader, Panel, PanelHeader } from '@/components/layout/PageHeader'
import { Alert } from '@/components/ui/alert'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { Button } from '@/components/ui/button'
import { DataTable, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/EmptyState'
import { Field } from '@/components/ui/field'
import { Modal } from '@/components/ui/Modal'
import { SelectField } from '@/components/ui/select'
import { Spinner } from '@/components/ui/Spinner'
import { StatTile } from '@/components/ui/StatTile'
import { StatusBadge, type Tone } from '@/components/ui/StatusBadge'
import { TextareaField } from '@/components/ui/textarea'
import { AgentName } from '@/features/agent-runs/agent-identity'
import type { Verdict } from '@/features/agent-runs/types'
import { VerdictCard } from '@/features/agent-runs/VerdictCard'

/**
 * Development-only reference (/design-system, not in production builds): every token and every
 * shared component in each of its states, so a change to the system can be checked in one place.
 * docs/design/TOKENS.md describes the same system in words.
 */
export default function DesignSystemPage() {
  const [modalOpen, setModalOpen] = useState(false)

  return (
    <div className="min-h-screen bg-surface-page">
      <div className="mx-auto max-w-[1440px] space-y-6 px-6 pb-16">
        <PageHeader
          title={
            <span className="flex items-center gap-3">
              <BrandMark size={36} /> AgriGuard design system
            </span>
          }
          description="Tokens and shared components in every state. Development builds only."
        />

        <div className="grid gap-4 xl:grid-cols-12">
          <Panel className="xl:col-span-8" aria-labelledby="ds-colour">
            <PanelHeader id="ds-colour" title="Colour" description="Canopy is the brand and primary action; earth is for soil, harvest and dealer surfaces; the page is warm white." />
            <SwatchRow name="Canopy" prefix="brand" steps={[50, 100, 200, 300, 400, 500, 600, 700, 800, 900]} />
            <SwatchRow name="Earth" prefix="earth" steps={[50, 100, 200, 300, 400, 500, 600, 700, 800]} />
            <div className="mt-3 grid grid-cols-3 gap-2 sm:grid-cols-6">
              {['surface-page', 'surface-card', 'surface-sunken', 'surface-inset', 'border-subtle', 'border-strong'].map((token) => (
                <Swatch key={token} token={token} />
              ))}
            </div>
            <div className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-4">
              {['success', 'warning', 'danger', 'info'].map((token) => (
                <Swatch key={token} token={token} />
              ))}
            </div>
            <p className="mt-3 text-xs text-stone-600">
              Contrast on white: brand-600 6.7:1 (text, primary buttons); brand-500 4.5:1 (large fills and icons only); warning 5.0:1; danger 6.6:1; info 4.4:1 (icons and borders only — text uses info-800, 8.9:1).
            </p>
          </Panel>

          <Panel className="xl:col-span-4" aria-labelledby="ds-type">
            <PanelHeader id="ds-type" title="Type" description="Source Serif 4 for titles and figures; Inter for everything else, with tabular figures." />
            <div className="space-y-2">
              <p className="font-display text-3xl font-semibold">Page title 30</p>
              <p className="font-display text-2xl font-semibold">Section 24</p>
              <p className="font-display text-xl font-semibold">Panel heading 20</p>
              <p className="text-base">Body on mobile 16</p>
              <p className="text-sm">Console body 14 — 0.6 L/ha, 1,250 kg, 2026-09-26</p>
              <p className="text-xs text-stone-600">Caption 12, the smallest size used</p>
            </div>
          </Panel>

          <Panel className="xl:col-span-12" aria-labelledby="ds-buttons">
            <PanelHeader id="ds-buttons" title="Buttons" description="Pressed buttons move down 1px over 80ms, unless reduced motion is asked for." />
            <div className="flex flex-wrap items-center gap-2">
              <Button>Primary</Button>
              <Button variant="secondary">Secondary</Button>
              <Button variant="ghost">Ghost</Button>
              <Button variant="danger">Danger</Button>
              <Button loading>Saving</Button>
              <Button disabled>Disabled</Button>
              <Button size="sm" variant="secondary">
                Small
              </Button>
              <Button size="lg">
                <Icons.CheckCircle /> Large with icon
              </Button>
            </div>
          </Panel>

          <Panel className="xl:col-span-6" aria-labelledby="ds-fields">
            <PanelHeader id="ds-fields" title="Fields" />
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Default" placeholder="Type here" />
              <Field label="With a unit" defaultValue="2.5" unit="kg/ha" hint="The most a hectare may receive per spray." />
              <Field label="With an error" defaultValue="-1" error="Enter a dose above zero." />
              <Field label="Disabled" defaultValue="0.800" disabled />
              <SelectField label="Select" defaultValue="t">
                <option value="t">Tomato</option>
                <option value="c">Chilli</option>
              </SelectField>
              <TextareaField label="Textarea" hint="Recorded in the audit trail." />
            </div>
          </Panel>

          <Panel className="xl:col-span-6" aria-labelledby="ds-status">
            <PanelHeader id="ds-status" title="Status and alerts" description="Every status has a word and an icon shape; colour only reinforces." />
            <div className="flex flex-wrap gap-2">
              {(['neutral', 'active', 'warning', 'done', 'danger'] as Tone[]).map((tone) => (
                <StatusBadge key={tone} label={tone} tone={tone} />
              ))}
            </div>
            <div className="mt-3 space-y-2">
              <Alert tone="success" title="Prescription issued">RX-2026-000002 is on the farmer's phone.</Alert>
              <Alert tone="info">Only a field agronomist can decide.</Alert>
              <Alert tone="warning" title="Spray window closing">Rain is likely after 14:00.</Alert>
              <Alert tone="error" title="Hard stop">V5: the pre-harvest interval would be broken.</Alert>
            </div>
          </Panel>

          <div className="grid gap-4 sm:grid-cols-2 xl:col-span-12 xl:grid-cols-4">
            <StatTile label="Awaiting approval" value="3" detail="Oldest reported 2 days ago" icon={<Icons.Workflow />} />
            <StatTile label="Disease pressure" value="42" detail="Late blight leads, rising" interactive />
            <StatTile label="Stock on the shelf" value="LKR 103,200" detail="1 out of stock, 2 running low" />
            <StatTile label="Slots booked" value="18 / 24" detail="Next 7 days" />
          </div>

          <Panel className="xl:col-span-4" aria-labelledby="ds-agents">
            <PanelHeader id="ds-agents" title="The four agents" description="One hue and one glyph each, everywhere they appear." />
            <ul className="space-y-2">
              {(['Coordinator', 'Diagnosis', 'Action', 'Validation'] as const).map((role) => (
                <li key={role}>
                  <AgentName role={role} />
                </li>
              ))}
            </ul>
          </Panel>

          <Panel className="xl:col-span-8" aria-labelledby="ds-icons">
            <PanelHeader id="ds-icons" title="Icons" description="24-unit stroke glyphs at 1.5 weight: 20px in navigation and buttons, 16px inline." />
            <ul className="grid grid-cols-6 gap-2 sm:grid-cols-10">
              {Object.entries(Icons).map(([name, Icon]) => (
                <li key={name} className="flex flex-col items-center gap-1 rounded-md bg-surface-sunken py-2 text-stone-800" title={name}>
                  <Icon />
                  <span className="max-w-full truncate px-1 text-xs text-stone-600">{name}</span>
                </li>
              ))}
            </ul>
          </Panel>

          <div className="space-y-4 xl:col-span-6">
            <AsyncBoundary isPending label="Loading the example" error={null}>
              {null}
            </AsyncBoundary>
            <AsyncBoundary isPending={false} error={new Error('x')} onRetry={() => undefined}>
              {null}
            </AsyncBoundary>
            <p className="flex items-center gap-2 text-sm text-stone-600">
              <Spinner /> Inline spinner
            </p>
          </div>

          <div className="xl:col-span-6">
            <EmptyState title="No plots yet" description="Add the fields you grow on." action={<Button>Add plot</Button>} />
          </div>

          <div className="space-y-2 xl:col-span-12">
            <DataTable caption="Example batches" columns={columns} rows={rows} rowKey={(r) => r.batch} sort={{ sortBy: 'expiry', desc: false }} onSortChange={() => undefined} />
          </div>

          <div className="xl:col-span-12">
            <VerdictCard verdict={rejectedVerdict} />
          </div>

          <Panel className="xl:col-span-12" aria-labelledby="ds-depth">
            <PanelHeader id="ds-depth" title="Depth and motion" description="Shadows are tinted canopy green. Hover the raised card; reduced motion keeps the shadow and drops the lift." />
            <div className="flex flex-wrap items-center gap-4">
              <div className="rounded-xl border border-border-subtle bg-surface-card p-4 shadow-raised">Raised</div>
              <div className="rounded-xl border border-border-subtle bg-surface-card p-4 shadow-lifted">Lifted</div>
              <div className="rounded-xl border border-border-subtle bg-surface-card p-4 shadow-overlay">Overlay</div>
              <div className="card-raised is-interactive rounded-xl border border-border-subtle bg-surface-card p-4">Hover me</div>
              <span className="drift inline-flex text-brand-600">
                <Icons.Leaf size={24} />
              </span>
              <span className="drift-slow inline-flex text-info">
                <Icons.Droplet size={24} />
              </span>
              <Button variant="secondary" onClick={() => setModalOpen(true)}>
                Open a modal
              </Button>
            </div>
          </Panel>
        </div>
      </div>
      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title="Close AG-2026-000004?" description="A closed case cannot be reopened.">
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={() => setModalOpen(false)}>
            Keep open
          </Button>
          <Button onClick={() => setModalOpen(false)}>Close case</Button>
        </div>
      </Modal>
    </div>
  )
}

function SwatchRow({ name, prefix, steps }: { name: string; prefix: string; steps: number[] }) {
  return (
    <div className="mt-2">
      <p className="mb-1 text-sm font-medium text-stone-800">{name}</p>
      <div className="grid grid-cols-5 gap-1 sm:grid-cols-10">
        {steps.map((step) => (
          <Swatch key={step} token={`${prefix}-${step}`} label={String(step)} />
        ))}
      </div>
    </div>
  )
}

function Swatch({ token, label }: { token: string; label?: ReactNode }) {
  return (
    <div>
      <div className="h-10 rounded-md ring-1 ring-black/5 ring-inset" style={{ background: `var(--color-${token})` }} />
      <p className="mt-0.5 truncate text-xs text-stone-600">{label ?? token}</p>
    </div>
  )
}

interface Row {
  batch: string
  product: string
  expiry: string
  onHand: number
  state: { label: string; tone: Tone }
}

const rows: Row[] = [
  { batch: 'MZ-2601', product: 'Mancozeb 80 WP', expiry: '2026-10-12', onHand: 12, state: { label: 'Expiring soon', tone: 'warning' } },
  { batch: 'AZ-2603', product: 'Azoxystrobin 25 SC', expiry: '2027-03-01', onHand: 40, state: { label: 'In date', tone: 'neutral' } },
  { batch: 'CL-2512', product: 'Chlorothalonil 75 WP', expiry: '2026-09-20', onHand: 3, state: { label: 'Expired', tone: 'danger' } },
]

const columns: Column<Row>[] = [
  { key: 'batch', header: 'Batch', sortable: true, render: (r) => <span className="font-medium">{r.batch}</span> },
  { key: 'product', header: 'Product', render: (r) => r.product },
  { key: 'expiry', header: 'Expiry', sortable: true, render: (r) => r.expiry },
  { key: 'onHand', header: 'On hand', numeric: true, render: (r) => r.onHand },
  { key: 'state', header: 'State', render: (r) => <StatusBadge label={r.state.label} tone={r.state.tone} /> },
]

/** A made-up verdict with every result shape: hard stops failed (V2, V5), a revisable failure, a rule not checked. */
const rejectedVerdict: Verdict = {
  outcome: 'Rejected',
  summary: 'Rejected: V2, V5 failed; V3 needs revision; not checked: V8.',
  results: [
    { code: 'V1', name: 'Proposal is well-formed', status: 'Passed', severity: 'Reject', message: 'All fields are present and well-formed.', evidence: null },
    { code: 'V2', name: 'Product is approved for this crop', status: 'Failed', severity: 'Reject', message: 'Carbofuran 3 GR is not approved for tomato.', evidence: 'product=carbofuran-3gr; crop=TOM' },
    { code: 'V3', name: 'Dose is within the approved range', status: 'Failed', severity: 'Revise', message: '1.2 L/ha is above the 0.4–0.8 L/ha range.', evidence: 'dose=1.2; range=0.4-0.8 L/ha' },
    { code: 'V4', name: 'Total quantity matches dose × plot area', status: 'Passed', severity: 'Revise', message: '0.96 matches 1.2 × 0.8 ha.', evidence: null },
    { code: 'V5', name: 'Pre-harvest interval is respected', status: 'Failed', severity: 'Reject', message: 'Harvest on 2026-10-02 is 5 days after spraying; this product needs 21.', evidence: 'spray=2026-09-27; phi=21d; harvest=2026-10-02' },
    { code: 'V6', name: 'Applications per cycle stay within the limit', status: 'Passed', severity: 'Reject', message: 'Used 0 of 2 times this cycle.', evidence: null },
    { code: 'V7', name: 'Interval since the last application is respected', status: 'Passed', severity: 'Revise', message: 'No earlier use this cycle.', evidence: null },
    { code: 'V8', name: 'Weather allows spraying', status: 'NotEvaluated', severity: 'Revise', message: 'Not checked: no weather forecast is available for the spray date.', evidence: null },
    { code: 'V9', name: 'Dealer stock is available', status: 'Passed', severity: 'Revise', message: 'Kandy Agro Supplies has 5 L available.', evidence: null },
    { code: 'V10', name: 'Proposal belongs to the case', status: 'Passed', severity: 'Reject', message: "The proposal is for the case's own crop cycle and farmer.", evidence: null },
    { code: 'V11', name: "Order is within the farmer's credit limit", status: 'Passed', severity: 'Revise', message: 'LKR 9,600 is within the LKR 50,000 limit.', evidence: null },
  ],
}
