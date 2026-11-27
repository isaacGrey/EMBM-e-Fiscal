import { useRef, useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { api, errorMessage, manilaTimestamp, sessionQuery } from '@/lib/account-api'
import { billingPeriods, catalogQuery, documentTypes, formatCount, type Billing, type CreditPackage, type Plan } from '@/lib/subscriptions'
import { Badge, Button, Card, ErrorState, Field, Input, LoadingState, Money, Select, Table } from '@/components/ui'
import './billing.css'

export const billingRoot = '/portal/client'
type Checkout = {
  id: string; subscriberId: string | null; purpose: string; amount: number; currency: string; provider: string
  providerPaymentId: string; status: string; confirmedAt: string | null; refundedAmount: number; failureReason: string | null
  plan: string; package: string | null
}
type HostedOrder = Checkout & { expiresAt: string; billingFrequency: Billing | ''; credits: number }
type Payment = Checkout & { createdAt: string }
type Lot = { id: string; source: string; granted: number; remaining: number; reserved: number; effectiveAt: string; expiresAt: string | null }
type Entry = {
  id: number; at: string; eventType: string; quantity: number; sourceReference: string
  companyId: string | null; documentId: string | null; documentType: string | null
}
type Page<T> = { page: number; total: number; items: T[] }
type AutoTopUp = {
  enabled: boolean; threshold: number; packageVersionId: string | null; maximumAmount: number; periodMaximumSpend: number
  consentAt: string | null; lastTopUpAt: string | null; lastFailure: string | null
}
type Subscription = { available: false } | {
  available: true; plan: Plan; status: string; renewalStatus: string | null; billingFrequency: Billing; startedAt: string; periodStart: string; periodEnd: string
  cancelAtPeriodEnd: boolean; renewalConsentAt: string | null; nextPlanVersionId: string | null; nextFrequency: Billing | null
  nextPlanName: string; renewalAmount: number; hasPaymentMethod: boolean; renewalAvailable: boolean; currentIncluded: number
  companies: { id: string; name: string }[]; users: number; topUp: AutoTopUp | null; packages: CreditPackage[]
  wallet: { available: number; included: number; purchased: number; reserved: number; used: number; expired: number; lots: Lot[] }
}
type ActiveSubscription = Extract<Subscription, { available: true }>
const labels: Record<string, string> = {
  InitialSubscription: 'Initial subscription', SubscriptionRenewal: 'Subscription renewal', CreditTopUp: 'Credit top-up',
  PastDue: 'Past due', GracePeriod: 'Grace period', PartiallyRefunded: 'Partially refunded', RefundReversal: 'Refund reversal', ReservationRelease: 'Reservation release',
  SubscriptionGrant: 'Subscription grant', RenewalGrant: 'Renewal grant', TopUpGrant: 'Purchased top-up', AuthorizedAdjustment: 'Authorized adjustment',
  SalesInvoice: 'Sales Invoice', CreditMemo: 'Credit Memo', SalesOrder: 'Sales Order',
}
const label = (value: string) => labels[value] ?? value
const date = (value?: string | null) => value ? manilaTimestamp.format(new Date(value)) : '—'
const checkoutPath = (id: string) => billingRoot + '/checkout/' + id
const subsection = (name: string) => billingRoot + '/' + name

function useBillingAction() {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  async function run(action: () => Promise<unknown>) {
    setBusy(true)
    setError('')
    try { await action() }
    catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  return { busy, error, run }
}

export function CheckoutButton({ base, purpose, packageId, children, disabled }: {
  base: string; purpose: string; packageId?: string; children: ReactNode; disabled?: boolean
}) {
  const navigate = useNavigate()
  const action = useBillingAction()
  const operation = useRef('')
  return <div className="billing-action">
    <Button disabled={action.busy || disabled} onClick={() => void action.run(async () => {
      operation.current ||= crypto.randomUUID()
      const order = await api<Checkout>(base + '/checkout', 'POST', { purpose, operationKey: operation.current, packageVersionId: packageId })
      navigate(checkoutPath(order.id))
    })}>{action.busy ? 'Opening checkout…' : children}</Button>
    {action.error && <ErrorState>{action.error}</ErrorState>}
  </div>
}

export function HostedCheckout({ id }: { id: string }) {
  const client = useQueryClient()
  const action = useBillingAction()
  const query = useQuery({ queryKey: ['checkout', id], queryFn: () => api<HostedOrder>('/billing/checkout/' + id), retry: false })
  if (query.isPending) return <LoadingState label="Loading checkout…" />
  if (query.isError) return <Card className="workspace-card"><ErrorState>{errorMessage(query.error)}</ErrorState>
    <Link className="text-link" to={billingRoot}>Return to workspace</Link></Card>
  const order = query.data
  const awaiting = ['Created', 'Pending', 'Processing'].includes(order.status)
  const initial = order.purpose === 'InitialSubscription'
  const returnTo = initial ? billingRoot : subsection(order.purpose === 'CreditTopUp' ? 'credits' : 'subscription')
  const outcomes = order.status === 'Processing'
    ? [['paid', 'Confirm delayed payment'], ['failed', 'Simulate failure'], ['cancelled', 'Cancel checkout']]
    : [['paid', 'Simulate successful payment'], ['duplicate', 'Simulate duplicate confirmation'], ['processing', 'Simulate delayed confirmation'], ['failed', 'Simulate failure'], ['cancelled', 'Cancel checkout']]
  function simulate(outcome: string) {
    return action.run(async () => {
      const result = await api<HostedOrder>('/billing/checkout/' + id + '/simulate/' + outcome, 'POST')
      client.setQueryData(['checkout', id], result)
      await Promise.all([client.invalidateQueries({ queryKey: ['workspace'] }), client.invalidateQueries({ queryKey: ['commercial'] }),
        client.invalidateQueries({ queryKey: sessionQuery.queryKey })])
    })
  }
  return <Card className="workspace-card billing-card">
    <Badge tone="warning">Development payment simulator</Badge>
    <h2>{label(order.purpose)}</h2>
    <p>Use this checkout with test accounts. Simulations carry no financial charge.</p>
    <dl className="workspace-definitions">
      <Fact name={order.package ? 'Credit package' : 'Subscription plan'}>{order.package ?? order.plan}</Fact>
      {order.billingFrequency && <Fact name="Billing cycle">{billingPeriods[order.billingFrequency].label}</Fact>}
      <Fact name="Save & Send Credits">{formatCount(order.credits)}</Fact>
      <Fact name="Amount"><Money amount={order.amount} /></Fact>
      <Fact name="Payment status">{label(order.status)}</Fact>
      <Fact name="Payment reference">{order.providerPaymentId}</Fact>
      <Fact name="Checkout expires">{date(order.expiresAt)}</Fact>
      {order.confirmedAt && <Fact name="Confirmed">{date(order.confirmedAt)}</Fact>}
      {order.refundedAmount > 0 && <Fact name="Refunded"><Money amount={order.refundedAmount} /></Fact>}
    </dl>
    {order.status === 'Processing' && <p role="status">Payment confirmation is pending. Confirm the delayed payment when ready.</p>}
    {order.status === 'Paid' && <p role="status">{initial ? 'Payment confirmed. Your subscriber account is ready for company onboarding.'
      : order.purpose === 'CreditTopUp' ? 'Payment confirmed. Purchased credits are available in your wallet.' : 'Payment confirmed. Your subscription has renewed.'}</p>}
    {order.failureReason && <ErrorState>{order.failureReason}</ErrorState>}
    {action.error && <ErrorState>{action.error}</ErrorState>}
    <div className="access-actions">
      {awaiting && outcomes.map(([outcome, title], index) => <Button key={outcome} variant={index === 0 ? 'primary' : 'light'}
        disabled={action.busy} onClick={() => void simulate(outcome)}>{title}</Button>)}
      {order.status === 'Paid' && <Button variant="light" disabled={action.busy} onClick={() => void simulate('refund')}>Simulate refund</Button>}
      <Button variant="light" asChild><Link to={returnTo}>{initial && order.status === 'Paid' ? 'Open client workspace' : 'Return to workspace'}</Link></Button>
    </div>
  </Card>
}

function Fact({ name, children }: { name: string; children: ReactNode }) {
  return <div><dt>{name}</dt><dd>{children}</dd></div>
}

export function BillingWorkspace({ subscriber, section }: { subscriber: string; section: string }) {
  const base = '/billing/subscribers/' + subscriber
  const summary = useQuery({ queryKey: ['commercial', subscriber, 'summary'], queryFn: () => api<Subscription>(base + '/summary'), retry: false })
  const catalog = useQuery(catalogQuery)
  if (summary.isPending) return <LoadingState label="Loading subscription and credits…" />
  if (summary.isError) return <ErrorState>{errorMessage(summary.error)}</ErrorState>
  if (!summary.data.available) return <Card className="workspace-card"><h2>Billing unavailable</h2>
    <p>This account has no paid subscription or Save &amp; Send wallet. Sign in with a provisioned subscriber to manage billing.</p></Card>
  const data = summary.data
  const refresh = async () => { await summary.refetch() }
  const checkout = catalog.data?.checkoutAvailable ?? false
  if (section === 'billing-history') return <BillingHistory base={base} />
  if (section === 'credits') return <CreditWallet base={base} data={data} checkout={checkout} refresh={refresh} />
  return <SubscriptionOverview base={base} data={data} plans={catalog.data?.plans ?? []} checkout={checkout} refresh={refresh} />
}

function SubscriptionOverview({ base, data, plans, checkout, refresh }: {
  base: string; data: ActiveSubscription; plans: Plan[]; checkout: boolean; refresh: () => Promise<unknown>
}) {
  const action = useBillingAction()
  const plan = data.plan
  const settings = (body: object) => action.run(async () => { await api(base + '/subscription', 'PUT', body); await refresh() })
  return <>
    <Card className="workspace-card billing-card">
      <div className="billing-heading"><h2>{plan.name} subscription</h2><Badge tone={data.status === 'Active' ? undefined : 'warning'}>{label(data.status)}</Badge></div>
      <dl className="workspace-definitions">
        <Fact name="Billing cycle">{billingPeriods[data.billingFrequency].label}</Fact>
        <Fact name="Subscription start">{date(data.startedAt)}</Fact>
        <Fact name="Current period ends">{date(data.periodEnd)}</Fact>
        <Fact name="Next billing date">{data.cancelAtPeriodEnd ? 'Cancelled at period end' : date(data.periodEnd)}</Fact>
        <Fact name="Renewal amount"><Money amount={data.renewalAmount} /></Fact>
        <Fact name="Automatic renewal">{data.renewalConsentAt ? 'Consent recorded' : 'Off'}</Fact>
        {data.renewalStatus && <Fact name="Latest renewal attempt">{label(data.renewalStatus)}</Fact>}
        <Fact name="Included Credits">{formatCount(data.currentIncluded)}</Fact>
        <Fact name="Available Credits">{formatCount(data.wallet.available)}</Fact>
        <Fact name="Company files">{data.companies.length} / {plan.companyFiles}</Fact>
        <Fact name="Authorized users">{data.users} / {plan.users}</Fact>
        <Fact name="Connector limit">{plan.accountingConnections}</Fact>
        <Fact name="Connector categories">{plan.connectorCategories.join(', ') || 'None'}</Fact>
      </dl>
      {!!plan.integrations.length && <p>Eligible connectors · {plan.integrations.map(id => connectorNames[id] ?? id).join(', ')}</p>}
      <div className="access-actions">
        <Button asChild><Link to={subsection('credits') + '#buy-credits'}>Buy Credits</Link></Button>
        <Button variant="light" asChild><Link to={subsection('billing-history')}>View Billing History</Link></Button>
        {checkout && data.renewalAvailable && <CheckoutButton base={base} purpose="SubscriptionRenewal">Renew subscription</CheckoutButton>}
      </div>
    </Card>
    <Card className="workspace-card billing-card">
      <h2>Change Plan</h2>
      <p>Changes apply after confirmation of the next renewal payment.</p>
      {data.nextPlanVersionId && <p role="status">Scheduled · {data.nextPlanName} · {data.nextFrequency && billingPeriods[data.nextFrequency].label}</p>}
      <form onSubmit={event => {
        event.preventDefault()
        const form = new FormData(event.currentTarget)
        void settings({ nextPlanVersionId: form.get('plan'), nextFrequency: form.get('frequency') })
      }}>
        <fieldset className="billing-fields" disabled={action.busy || !plans.length}>
          <Field id="next-plan" label="Next plan"><Select id="next-plan" name="plan" defaultValue={data.nextPlanVersionId ?? plan.versionId} required>
            {plans.map(item => <option key={item.versionId} value={item.versionId}>{item.name}</option>)}</Select></Field>
          <Field id="next-frequency" label="Next billing cycle"><Select id="next-frequency" name="frequency" defaultValue={data.nextFrequency ?? data.billingFrequency}>
            <option value="monthly">Monthly</option><option value="annual">Annual</option></Select></Field>
          <Button type="submit">Schedule plan change</Button>
        </fieldset>
      </form>
      {data.nextPlanVersionId && <Button variant="light" disabled={action.busy} onClick={() => void settings({ clearChange: true })}>Clear scheduled change</Button>}
    </Card>
    <Card className="workspace-card billing-card">
      <h2>Manage Renewal and Payment Method</h2>
      <p>{data.hasPaymentMethod ? 'A simulated payment method is saved.' : 'No payment method is saved.'} Live recurring charges are unavailable.</p>
      {data.renewalConsentAt && <p>Renewal consent recorded {date(data.renewalConsentAt)}.</p>}
      <div className="access-actions">
        {checkout && <Button variant="light" disabled={action.busy}
          onClick={() => void settings({ storeDemoMethod: !data.hasPaymentMethod })}>{data.hasPaymentMethod ? 'Remove demo payment method' : 'Add demo payment method'}</Button>}
        {(checkout || data.renewalConsentAt) && (data.renewalConsentAt
          ? <Button variant="light" disabled={action.busy} onClick={() => void settings({ autoRenew: false })}>Withdraw renewal consent</Button>
          : <Button variant="light" disabled={action.busy || !data.hasPaymentMethod || !plan.renewal || data.cancelAtPeriodEnd}
            onClick={() => void settings({ autoRenew: true })}>Authorize automatic renewal</Button>)}
        <Button variant="light" disabled={action.busy} onClick={() => void settings({ cancelAtPeriodEnd: !data.cancelAtPeriodEnd })}>
          {data.cancelAtPeriodEnd ? 'Resume renewal' : 'Cancel at period end'}
        </Button>
      </div>
      {action.error && <ErrorState>{action.error}</ErrorState>}
    </Card>
  </>
}

const connectorNames: Record<string, string> = {
  autocount: 'AutoCount Accounting', qbd: 'QuickBooks Desktop', qbo: 'QuickBooks Online',
  accountingsuite: 'AccountingSuite / 1C', shopee: 'Shopee', tiktok: 'TikTok Shop',
}
function CreditWallet({ base, data, checkout, refresh }: {
  base: string; data: ActiveSubscription; checkout: boolean; refresh: () => Promise<unknown>
}) {
  const { wallet } = data
  const granted = wallet.lots.reduce((sum, lot) => sum + lot.granted, 0)
  return <>
    <Card className="workspace-card billing-card">
      <h2>Wallet overview</h2>
      <dl className="workspace-definitions">
        <Fact name="Available Credits">{formatCount(wallet.available)}</Fact>
        <Fact name="Included Credits remaining">{formatCount(wallet.included)}</Fact>
        <Fact name="Purchased Credits remaining">{formatCount(wallet.purchased)}</Fact>
        <Fact name="Reserved Credits">{formatCount(wallet.reserved)}</Fact>
        <Fact name="Used Credits">{formatCount(wallet.used)}</Fact>
        <Fact name="Expired Credits">{formatCount(wallet.expired)}</Fact>
        <Fact name="Credit utilization">{granted ? Math.round(wallet.used / granted * 100) : 0}% of granted credits used</Fact>
      </dl>
      <p>Authorized company files share this wallet. Sales Order saving uses zero credits.</p>
      <a className="text-link" href="#buy-credits">Buy Credits</a>
    </Card>
    <BuyCredits data={data} checkout={checkout} base={base} />
    <AutoTopUpForm base={base} data={data} refresh={refresh} />
    <Card className="workspace-card billing-card">
      <h2>Credit grants and expiration</h2>
      <Rows empty={!wallet.lots.length} label="Credit grant breakdown" headings={['Source', 'Effective date', 'Granted', 'Remaining', 'Reserved', 'Expires']}>
        {wallet.lots.map(lot => <tr key={lot.id}><td>{lot.source}</td><td>{date(lot.effectiveAt)}</td><td>{formatCount(lot.granted)}</td>
          <td>{formatCount(lot.remaining)}</td><td>{formatCount(lot.reserved)}</td><td>{lot.expiresAt ? date(lot.expiresAt) : 'No expiry'}</td></tr>)}
      </Rows>
    </Card>
    <CreditHistory base={base} companies={data.companies} />
  </>
}

function BuyCredits({ data, checkout, base }: { data: ActiveSubscription; checkout: boolean; base: string }) {
  const [selected, setSelected] = useState('')
  const item = data.packages.find(pkg => pkg.versionId === selected)
  const eligible = ['Active', 'GracePeriod'].includes(data.status)
  return <Card className="workspace-card billing-card" id="buy-credits">
    <h2>Buy Credits</h2>
    <p>{checkout ? 'Select a package and review its terms before opening the payment simulator.' : 'Online purchases are unavailable in this environment.'}</p>
    {checkout && !eligible && <p>Renew your subscription to buy credits.</p>}
    {data.packages.length > 0 && <Field id="credit-package" label="Credit package"><Select id="credit-package" value={selected} onChange={event => setSelected(event.target.value)}>
      <option value="">Select a package</option>{data.packages.map(pkg => <option key={pkg.versionId} value={pkg.versionId}>{pkg.name}</option>)}
    </Select></Field>}
    {!data.packages.length && <p>No packages are currently available for this plan.</p>}
    {item && <>
      <dl className="workspace-definitions"><Fact name="Credits">{formatCount(item.credits)}</Fact>
        <Fact name="Package price"><Money amount={item.price} /></Fact>
        <Fact name="Validity">{item.validityDays ? item.validityDays + ' days from confirmation' : 'No expiry'}</Fact></dl>
      {checkout && eligible && <CheckoutButton key={item.versionId} base={base} purpose="CreditTopUp" packageId={item.versionId}>Proceed to checkout</CheckoutButton>}
    </>}
  </Card>
}

function AutoTopUpForm({ base, data, refresh }: { base: string; data: ActiveSubscription; refresh: () => Promise<unknown> }) {
  const action = useBillingAction()
  const saved = data.topUp
  const canEnable = data.plan.autoTopUps && data.hasPaymentMethod && !data.cancelAtPeriodEnd && ['Active', 'GracePeriod'].includes(data.status)
  const [enabled, setEnabled] = useState(saved?.enabled ?? false)
  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const fields = new FormData(event.currentTarget)
    void action.run(async () => {
      await api(base + '/auto-top-up', 'PUT', { enabled, threshold: Number(fields.get('threshold')),
        packageVersionId: fields.get('package') || null, maximumAmount: Number(fields.get('maximum')),
        periodMaximumSpend: Number(fields.get('period')), consent: fields.get('consent') === 'on' })
      await refresh()
    })
  }
  return <Card className="workspace-card billing-card">
    <h2>Configure Auto Top-Up</h2>
    <p>Record an eligible plan’s top-up preferences and consent. Automatic charges are unavailable.</p>
    {!canEnable && <p>Enabling requires a current eligible plan and an authorized payment method. <Link to={subsection('subscription')} className="text-link">Manage subscription</Link></p>}
    {saved?.consentAt && <p>Consent recorded {date(saved.consentAt)}.</p>}
    {saved?.lastTopUpAt && <p>Last automatic top-up {date(saved.lastTopUpAt)}.</p>}
    {saved?.lastFailure && <ErrorState>{saved.lastFailure}</ErrorState>}
    <form onSubmit={save}><fieldset className="workspace-stack" disabled={action.busy}>
      <label className="acknowledgement"><input type="checkbox" checked={enabled} disabled={!canEnable && !enabled}
        onChange={event => setEnabled(event.target.checked)} /> Enable automatic top-up preference</label>
      {enabled && <div className="billing-fields">
        <Field id="top-up-threshold" label="Credit threshold"><Input id="top-up-threshold" name="threshold" type="number" min="0" defaultValue={saved?.threshold ?? 0} required /></Field>
        <Field id="top-up-package" label="Automatic top-up package"><Select id="top-up-package" name="package" defaultValue={saved?.packageVersionId ?? ''} required>
          <option value="">Select a package</option>{data.packages.map(pkg => <option key={pkg.versionId} value={pkg.versionId}>{pkg.name}</option>)}</Select></Field>
        <Field id="top-up-max" label="Maximum PHP per charge"><Input id="top-up-max" name="maximum" type="number" min=".01" step=".01" defaultValue={saved?.maximumAmount} required /></Field>
        <Field id="top-up-period" label="Maximum PHP per billing period"><Input id="top-up-period" name="period" type="number" min=".01" step=".01" defaultValue={saved?.periodMaximumSpend} required /></Field>
        <label className="acknowledgement"><input type="checkbox" name="consent" required /> I authorize top-ups under these limits when the payment provider supports them.</label>
      </div>}
      <div><Button type="submit">Save top-up settings</Button></div>
    </fieldset></form>
    {action.error && <ErrorState>{action.error}</ErrorState>}
  </Card>
}

function Rows({ empty, label: title, headings, children }: { empty: boolean; label: string; headings: string[]; children: ReactNode }) {
  return empty ? <p>No entries match this view.</p> : <div className="billing-table" role="region" aria-label={title} tabIndex={0}>
    <Table><thead><tr>{headings.map(name => <th scope="col" key={name}>{name}</th>)}</tr></thead><tbody>{children}</tbody></Table>
  </div>
}

function Pager({ page, total, change }: { page: number; total: number; change: (number: number) => void }) {
  if (total <= 25 && page === 1) return null
  return <div className="access-actions" aria-label="Pagination">
    <Button variant="light" disabled={page <= 1} onClick={() => change(page - 1)}>Previous page</Button>
    <span>Page {page} of {Math.max(1, Math.ceil(total / 25))}</span>
    <Button variant="light" disabled={page * 25 >= total} onClick={() => change(page + 1)}>Next page</Button>
  </div>
}

function CreditHistory({ base, companies }: { base: string; companies: ActiveSubscription['companies'] }) {
  const [filters, setFilters] = useState({ from: '', through: '', company: '', documentType: '', search: '' })
  const [page, setPage] = useState(1)
  const query = new URLSearchParams(Object.entries(filters).filter(([, value]) => value !== ''))
  const url = base + '/credits?' + query
  const history = useQuery({ queryKey: ['commercial', url, page], queryFn: () => api<Page<Entry>>(url + '&page=' + page), retry: false })
  const change = (key: keyof typeof filters, value: string) => { setPage(1); setFilters(previous => ({ ...previous, [key]: value })) }
  return <Card className="workspace-card billing-card">
    <h2>Credit Usage History</h2>
    <div className="billing-fields">
      <Field id="usage-from" label="From date"><Input id="usage-from" type="date" value={filters.from} onChange={e => change('from', e.target.value)} /></Field>
      <Field id="usage-through" label="Through date"><Input id="usage-through" type="date" value={filters.through} onChange={e => change('through', e.target.value)} /></Field>
      <Field id="usage-company" label="Company file"><Select id="usage-company" value={filters.company} onChange={e => change('company', e.target.value)}>
        <option value="">All companies</option>{companies.map(company => <option key={company.id} value={company.id}>{company.name}</option>)}</Select></Field>
      <Field id="usage-document" label="Document type"><Select id="usage-document" value={filters.documentType} onChange={e => change('documentType', e.target.value)}>
        <option value="">All documents</option>{documentTypes.filter(type => type.credits).map(type => <option key={type.key} value={type.name.replaceAll(' ', '')}>{type.name}</option>)}</Select></Field>
      <Field id="usage-search" label="Search credit history"><Input id="usage-search" type="search" maxLength={128} value={filters.search} onChange={e => change('search', e.target.value)} /></Field>
    </div>
    <div><a className="text-link" href={'/api' + url + '&export=true'} download>Export credit history (CSV)</a></div>
    {history.isPending ? <LoadingState /> : history.isError ? <ErrorState>{errorMessage(history.error)}</ErrorState> : <>
      <Rows empty={!history.data.items.length} label="Credit events" headings={['Date', 'Event', 'Credits', 'Source', 'Company', 'Document']}>
        {history.data.items.map(row => <tr key={row.id}><td>{date(row.at)}</td><td>{label(row.eventType)}</td><td>{formatCount(row.quantity)}</td>
          <td>{row.sourceReference}</td><td>{companies.find(company => company.id === row.companyId)?.name ?? '—'}</td>
          <td>{row.documentType && label(row.documentType)} {row.documentId ?? '—'}</td></tr>)}
      </Rows>
      <Pager page={page} total={history.data.total} change={setPage} />
    </>}
  </Card>
}

function BillingHistory({ base }: { base: string }) {
  const [status, setStatus] = useState('')
  const [purpose, setPurpose] = useState('')
  const [selected, setSelected] = useState<Payment | null>(null)
  const [page, setPage] = useState(1)
  const query = useQuery({ queryKey: ['commercial', base, 'payments', status, purpose, page],
    queryFn: () => api<Page<Payment>>(base + '/payments?' + new URLSearchParams({ status, purpose, page: String(page) })), retry: false })
  return <Card className="workspace-card billing-card">
    <h2>Payment transactions</h2>
    <div className="billing-fields">
      <Field id="payment-status" label="Payment status"><Select id="payment-status" value={status} onChange={event => { setStatus(event.target.value); setPage(1) }}>
        <option value="">All statuses</option>{['Pending', 'Processing', 'Paid', 'Failed', 'Cancelled', 'Expired', 'PartiallyRefunded', 'Refunded', 'Disputed']
          .map(value => <option key={value} value={value}>{label(value)}</option>)}</Select></Field>
      <Field id="payment-purpose" label="Payment purpose"><Select id="payment-purpose" value={purpose} onChange={event => { setPurpose(event.target.value); setPage(1) }}>
        <option value="">All purposes</option>{['InitialSubscription', 'SubscriptionRenewal', 'CreditTopUp']
          .map(value => <option key={value} value={value}>{label(value)}</option>)}</Select></Field>
    </div>
    {query.isPending ? <LoadingState /> : query.isError ? <ErrorState>{errorMessage(query.error)}</ErrorState> : <>
      <Rows empty={!query.data.items.length} label="Billing history" headings={['Date', 'Purpose', 'Plan', 'Package', 'Amount', 'Provider', 'Status', 'Reference']}>
        {query.data.items.map(item => <tr key={item.id}><td>{date(item.createdAt)}</td><td>{label(item.purpose)}</td><td>{item.plan}</td><td>{item.package ?? '—'}</td>
          <td><Money amount={item.amount} /> {item.currency}</td><td>{item.provider === 'DevelopmentMock' ? 'Development simulator' : item.provider}</td>
          <td>{label(item.status)}</td><td><button type="button" className="text-link" onClick={() => setSelected(item)}>{item.providerPaymentId}</button></td></tr>)}
      </Rows>
      <Pager page={page} total={query.data.total} change={setPage} />
    </>}
    {selected && <div className="billing-confirmation" aria-live="polite">
      <h3>{selected.provider === 'DevelopmentMock' ? 'Development payment record' : 'Payment record'}</h3>
      <p>{selected.providerPaymentId}</p>
      <dl className="workspace-definitions">
        <Fact name="Status">{label(selected.status)}</Fact><Fact name="Confirmed">{date(selected.confirmedAt)}</Fact>
        <Fact name="Refunded"><Money amount={selected.refundedAmount} /></Fact>
      </dl>
      {selected.failureReason && <p>{selected.failureReason}</p>}
      {selected.provider === 'DevelopmentMock' && <p>Simulation record for testing.</p>}
      <Button variant="light" onClick={() => setSelected(null)}>Close payment record</Button>
    </div>}
  </Card>
}

