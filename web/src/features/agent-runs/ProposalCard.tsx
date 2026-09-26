import type { IssuedPrescription, Proposal } from './types'

/** What the Action agent proposed. Its justification is shown as the model's words, nothing more. */
export function ProposalCard({ proposal, productName }: { proposal: Proposal; productName: string | null }) {
  return (
    <section aria-labelledby="proposal-heading" className="space-y-3 rounded-lg border border-stone-200 bg-white p-4">
      <h2 id="proposal-heading" className="font-medium text-stone-900">
        Proposed treatment
      </h2>
      <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
        <Item label="Product" value={productName ?? proposal.product_id} />
        <Item label="Dose" value={`${proposal.dose_per_hectare} per ha`} />
        <Item label="Total quantity" value={String(proposal.total_quantity)} />
        <Item label="Spray date" value={proposal.spray_date} />
      </dl>
      <p className="text-sm text-stone-600">
        {proposal.dealer_id ? 'From the dealer the agent chose.' : 'From the best-stocked dealer in the district.'}
      </p>
      <p className="text-xs text-stone-600">
        <span className="font-medium">Agent's justification:</span> {proposal.justification}
      </p>
    </section>
  )
}

/** Shown once approved: what the farmer will act on and collect. */
export function PrescriptionCard({ prescription }: { prescription: IssuedPrescription }) {
  return (
    <section aria-labelledby="rx-heading" className="space-y-3 rounded-lg border border-sky-200 bg-sky-50 p-4">
      <header>
        <h2 id="rx-heading" className="font-medium text-sky-900">
          Prescription {prescription.prescriptionNo} issued
        </h2>
        <p className="text-sm text-sky-900">
          Order {prescription.orderNo} confirmed with {prescription.dealerName}.
        </p>
      </header>
      <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-3">
        <Item label="Product" value={prescription.productName} />
        <Item label="Dose" value={`${prescription.dosePerHectare} per ha`} />
        <Item label="Spray date" value={prescription.sprayDate} />
        <Item label="Do not harvest before" value={prescription.earliestSafeHarvestDate} />
        <Item label="Packs" value={String(prescription.packs)} />
        <Item label="Order total" value={`LKR ${prescription.orderTotal.toLocaleString('en-US')}`} />
      </dl>
      {prescription.instructions && <p className="text-sm text-sky-900">{prescription.instructions}</p>}
    </section>
  )
}

function Item({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-stone-500">{label}</dt>
      <dd className="font-medium text-stone-900">{value}</dd>
    </div>
  )
}
