import type { ReactNode } from 'react'
import { Panel, PanelHeader } from '@/components/layout/PageHeader'
import { AlertTriangle, CheckCircle } from '@/components/icons'
import { cn } from '@/lib/utils'
import { AgentGlyph } from './agent-identity'
import { formatAmount, formatDose } from './format'
import type { IssuedPrescription, Proposal } from './types'
import type { ProductUnit } from '@/features/inventory/types'

/** What the Action agent proposed. Its justification is shown as the model's words, nothing more. */
export function ProposalCard({
  proposal,
  productName,
  productUnit,
}: {
  proposal: Proposal
  productName: string | null
  productUnit: ProductUnit | null
}) {
  return (
    <Panel aria-labelledby="proposal-heading">
      <PanelHeader
        id="proposal-heading"
        title="Proposed treatment"
        icon={<AgentGlyph role="Action" />}
        description={proposal.dealer_id ? 'From the dealer the agent chose.' : 'From the best-stocked dealer in the district.'}
      />
      <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-lg border border-border-subtle bg-border-subtle">
        <Figure label="Product" value={productName ?? proposal.product_id} wide />
        <Figure label="Dose" value={formatDose(proposal.dose_per_hectare, productUnit)} />
        <Figure label="Total quantity" value={formatAmount(proposal.total_quantity, productUnit)} />
        <Figure label="Spray date" value={proposal.spray_date} wide />
      </dl>
      <p className="mt-3 text-xs text-stone-700">
        <span className="font-semibold">Agent's justification:</span> {proposal.justification}
      </p>
    </Panel>
  )
}

/** Shown once approved: what the farmer will act on and collect. The harvest date is the warning. */
export function PrescriptionCard({ prescription }: { prescription: IssuedPrescription }) {
  return (
    <Panel raised aria-labelledby="rx-heading" className="border-success-200">
      <PanelHeader
        id="rx-heading"
        icon={<CheckCircle className="text-success" />}
        title={`Prescription ${prescription.prescriptionNo} issued`}
        description={`Order ${prescription.orderNo} confirmed with ${prescription.dealerName}.`}
      />
      <div className="mb-3 flex items-start gap-2.5 rounded-lg border border-warning-200 bg-warning-50 px-3 py-2.5 text-warning-800">
        <AlertTriangle size={20} className="mt-0.5 shrink-0 text-warning" />
        <dl>
          <dt className="text-sm font-semibold">Do not harvest before</dt>
          <dd className="font-display text-2xl leading-tight font-semibold">{prescription.earliestSafeHarvestDate}</dd>
        </dl>
      </div>
      <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-lg border border-border-subtle bg-border-subtle">
        <Figure label="Product" value={prescription.productName} wide />
        <Figure label="Dose" value={formatDose(prescription.dosePerHectare, prescription.unit)} />
        <Figure label="Total to spray" value={formatAmount(prescription.totalQuantity, prescription.unit)} />
        <Figure label="Spray date" value={prescription.sprayDate} />
        <Figure label="Packs" value={String(prescription.packs)} />
        <Figure label="Order total" value={`LKR ${prescription.orderTotal.toLocaleString('en-US')}`} wide />
      </dl>
      {prescription.instructions && <p className="mt-3 text-sm text-stone-800">{prescription.instructions}</p>}
    </Panel>
  )
}

function Figure({ label, value, wide = false }: { label: string; value: ReactNode; wide?: boolean }) {
  return (
    <div className={cn('bg-surface-card px-3 py-2', wide && 'col-span-2')}>
      <dt className="text-xs text-stone-600">{label}</dt>
      <dd className="mt-0.5 font-semibold text-stone-900 tabular-nums">{value}</dd>
    </div>
  )
}
