import { useQuery } from '@tanstack/react-query'
import { errorMessage } from '@/lib/account-api'
import { ArrowRight, Check, Wallet } from 'lucide-react'
import { Link, useSearchParams } from 'react-router-dom'
import { CreditEstimator } from '@/components/credit-estimator'
import { PageIntro, SectionIntro } from '@/components/public-content'
import { Badge, Button, Card, ErrorState, LoadingState, Money, Table } from '@/components/ui'
import { integrations } from '@/lib/marketing-content'
import { billingPeriods, catalogQuery, documentTypes, formatCount, isBilling, registrationUrl, type Billing, type CreditPackage, type Plan } from '@/lib/subscriptions'
import type { ReactNode } from 'react'

function BillingSelector({ billing, select }: { billing: Billing; select: (billing: Billing) => void }) {
  return <fieldset className="billing-selector">
    <legend>Billing frequency</legend>
    <div>{Object.entries(billingPeriods).map(([key, period]) => {
      if (!isBilling(key)) return null
      return <label key={key}><input type="radio" name="billing" value={key} checked={billing === key}
        onChange={() => select(key)} /><span>{period.label}</span></label>
    })}</div>
  </fieldset>
}

function PlanCards({ billing, plans }: { billing: Billing; plans: Plan[] }) {
  const { unit } = billingPeriods[billing]
  return <div className="subscription-grid">{plans.map(plan => {
    const offer = plan.billing[billing]
    return <Card className="subscription-card" key={plan.id}>
      <h2>{plan.name}</h2><p>{plan.description}</p>
      <div className="subscription-price"><strong><Money amount={offer.price} /></strong><span> / {unit}</span></div>
      <dl className="subscription-limits">
        <div><dt>Included Credits / {unit}</dt><dd>{formatCount(offer.credits)}</dd></div>
        <div><dt>Company files</dt><dd>{formatCount(plan.companyFiles)}</dd></div>
        <div><dt>Authorized users</dt><dd>{formatCount(plan.users)}</dd></div>
      </dl>
      <ul className="subscription-capabilities">{plan.capabilities.map(capability => <li key={capability}><Check size={16} />{capability}</li>)}</ul>
      <Button asChild><Link to={registrationUrl(plan, billing)} aria-label={'Select Plan: ' + plan.name}>Select Plan <ArrowRight size={15} /></Link></Button>
    </Card>
  })}</div>
}

function PlanComparison({ billing, plans }: { billing: Billing; plans: Plan[] }) {
  const { unit } = billingPeriods[billing]
  const rows: { label: string; value: (plan: (typeof plans)[number]) => ReactNode }[] = [
    { label: 'Price / ' + unit, value: plan => <Money amount={plan.billing[billing].price} /> },
    { label: 'Included Credits / ' + unit, value: plan => formatCount(plan.billing[billing].credits) },
    { label: 'Company files', value: plan => formatCount(plan.companyFiles) },
    { label: 'Authorized users', value: plan => formatCount(plan.users) },
    { label: 'Automatic renewal', value: plan => plan.renewal ? 'Included' : '—' },
    { label: 'Optional automatic top-ups', value: plan => plan.autoTopUps ? 'Eligible' : '—' },
    ...integrations.map(item => ({ label: item.name, value: (plan: (typeof plans)[number]) => plan.integrations.includes(item.id) ? item.status : '—' })),
  ]
  return <section className="section section-tint" aria-labelledby="comparison-title">
    <div className="container">
      <div className="section-intro">
        <span className="eyebrow">PLAN ENTITLEMENTS</span><h2 id="comparison-title">Compare subscriptions</h2>
        <p>Connector cells show development status where the illustrative plan includes eligibility. A dash means the feature is outside that plan.</p>
      </div>
      <div className="comparison-scroll" role="region" aria-label="Scrollable subscription comparison" tabIndex={0}>
        <Table aria-label="Subscription comparison" className="comparison-table">
          <thead><tr><th scope="col">Allowance or entitlement</th>{plans.map(plan => <th key={plan.id} scope="col">{plan.name}</th>)}</tr></thead>
          <tbody>{rows.map(({ label, value }) => <tr key={label}><th scope="row">{label}</th>
            {plans.map(plan => <td key={plan.id}>{value(plan)}</td>)}</tr>)}</tbody>
        </Table>
      </div>
    </div>
  </section>
}

function CreditTopUps({ packages }: { packages: CreditPackage[] }) {
  return <section className="section section-tint" id="top-ups"><div className="container">
    <SectionIntro label="ADDITIONAL CREDITS" title="Buy Credits" text="Review credit packages. Eligible subscribers can try purchases and configure automatic top-up consent in the development portal." />
    <div className="top-up-grid">{packages.map(item => <Card className="top-up-card" key={item.versionId}>
      <Wallet size={19} /><div><b>{formatCount(item.credits)} Save &amp; Send Credits</b><span><Money amount={item.price} /> · illustrative</span></div>
    </Card>)}</div>
    <ul className="pricing-points">
      <li><Check size={17} />Only a successful Save &amp; Send for a Sales Invoice or applicable Credit Memo uses credits. Failed validation and other failures before commitment use zero credits.</li>
      <li><Check size={17} />Sales Order saving, accounting synchronization, resending, and delivery or reporting retries use zero credits. A delivery failure after commitment leaves the original charge in place.</li>
      <li><Check size={17} />Historical committed records stay accessible according to applicable retention and access requirements.</li>
    </ul>
  </div></section>
}

export function Pricing() {
  const [params, setParams] = useSearchParams()
  const catalog = useQuery(catalogQuery)
  const requested = params.get('billing')
  const billing = isBilling(requested) ? requested : 'monthly'
  return <main id="main-content">
    <PageIntro label="SUBSCRIPTIONS & SAVE & SEND CREDITS" title="Plans for your invoicing volume">
      Compare Save &amp; Send Credits, company files, and team access. Tier names, prices, and planned entitlements are illustrative.
    </PageIntro>
    <section className="section plan-section"><div className="container">
      <div className="pricing-toolbar"><Badge tone="warning">Illustrative pricing</Badge>
        <BillingSelector billing={billing} select={value => setParams({ billing: value }, { replace: true, preventScrollReset: true })} />
        <div className="pricing-tools">
          <a className="text-link" href="#estimator">Estimate credits <ArrowRight size={15} /></a>
          <a className="text-link" href="#top-ups">Buy Credits <ArrowRight size={15} /></a>
        </div>
      </div>
      {catalog.isPending ? <LoadingState label="Loading published plans…" /> : catalog.isError
        ? <ErrorState>{errorMessage(catalog.error)}</ErrorState>
        : catalog.data.plans.length ? <PlanCards billing={billing} plans={catalog.data.plans} />
        : <p>No plans are currently published.</p>}
      <p className="content-note">{billingPeriods[billing].label} amounts and Included Credits apply to the full billing period. Selecting a plan begins account registration. Payment is a separate step.</p>
    </div></section>
    {!!catalog.data?.plans.length && <PlanComparison billing={billing} plans={catalog.data.plans} />}
    <section className="section" id="estimator"><div className="container pricing-grid">
      <div><SectionIntro label="SAVE & SEND CREDITS" title="Estimate your usage" text="Credits count successfully committed Save & Send operations. These are the default rates." />
        <dl className="credit-rates">{documentTypes.map(type => <div key={type.key}>
          <dt>{type.name}<small>{type.action === 'CommitAndSend' ? 'Save & Send' : 'Save'}</small></dt>
          <dd>{type.credits} {type.credits === 1 ? 'credit' : 'credits'}</dd>
        </div>)}</dl>
        <p className="content-note">Save commits a Sales Order without using credits, even when Available Credits are zero. Subscription entitlements and permissions still apply. Sales Orders do not enter electronic invoice delivery or BIR reporting.</p>
      </div>
      <CreditEstimator billing={billing} />
    </div></section>
    <CreditTopUps packages={catalog.data?.packages ?? []} />
  </main>
}
