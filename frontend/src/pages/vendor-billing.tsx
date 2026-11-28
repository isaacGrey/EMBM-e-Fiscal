import { useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, errorMessage, manilaTimestamp } from '@/lib/account-api'
import { Button, Card, ErrorState, Field, Input, LoadingState, Money, Select, Table } from '@/components/ui'
import './billing.css'

type Version = { id: string; code: string; name: string; version: number; active: boolean; effectiveAt: string }
type PlanVersion = Version & { description: string; monthlyPrice: number; annualPrice: number; monthlyCredits: number; annualCredits: number
  allocationFrequency: string; companyLimit: number; userLimit: number; connectorLimit: number; integrations: string[]
  connectorCategories: string[]; features: string[]; creditValidityDays: number | null; rollover: boolean
  renewalAllowed: boolean; autoTopUpAllowed: boolean; graceDays: number }
type PackageVersion = Version & { credits: number; price: number; validityDays: number | null; eligiblePlans: string[] }
type Catalog = { plans: PlanVersion[]; packages: PackageVersion[] }
type Access = { canViewCatalog: boolean; canPublish: boolean; canMonitor: boolean; canAdjust: boolean }
type Monitor = {
  total: number; subscriptions: { subscriberId: string; name: string; plan: string; status: string; periodEnd: string }[]
  payments: { id: string; purpose: string; status: string; amount: number; providerPaymentId: string; failureReason: string | null }[]
  wallets: { id: string; subscriberId: string; available: number; projectionMismatch: boolean }[]
}
const money = (raw: FormData, field: string) => raw.get(field)
const num = (raw: FormData, field: string) => Number(raw.get(field))
const days = (raw: FormData, field: string) => raw.get(field) ? num(raw, field) : null
const list = (raw: FormData, field: string) => String(raw.get(field) ?? '').split(',').map(value => value.trim()).filter(Boolean)
const flag = (raw: FormData, field: string) => raw.get(field) === 'on'
const date = (value: string) => manilaTimestamp.format(new Date(value))
const catalogKey = ['vendor-commercial', 'catalog']

export function VendorBilling() {
  const access = useQuery({ queryKey: ['vendor-commercial', 'access'], queryFn: () => api<Access>('/vendor/billing/access'), retry: false })
  const catalog = useQuery({ queryKey: catalogKey, queryFn: () => api<Catalog>('/vendor/billing/catalog'), retry: false,
    enabled: !!access.data?.canViewCatalog })
  const [page, setPage] = useState(1)
  const monitor = useQuery({ queryKey: ['vendor-commercial', 'monitor', page],
    queryFn: () => api<Monitor>('/vendor/billing/monitor?page=' + page), retry: false, enabled: !!access.data?.canMonitor })
  if (access.isPending) return <LoadingState />
  if (access.isError) return <ErrorState>{errorMessage(access.error)}</ErrorState>
  if (!access.data.canViewCatalog && !access.data.canMonitor) return <p>Commercial administration requires an assigned billing grant.</p>
  return <section className="workspace-stack vendor-commercial" aria-label="Commercial management">
    {access.data.canViewCatalog && (catalog.isPending ? <LoadingState /> : catalog.isError ? <ErrorState>{errorMessage(catalog.error)}</ErrorState> : <>
      <Card className="workspace-card billing-card"><h2>Plan and credit package versions</h2>
        <VersionTable items={catalog.data.plans} />
        <VersionTable items={catalog.data.packages} />
      </Card>
      {access.data.canPublish && <><VersionForm kind="plans" versions={catalog.data.plans} />
        <VersionForm kind="packages" versions={catalog.data.packages} /></>}
    </>)}
    {access.data.canMonitor && <Card className="workspace-card billing-card"><h2>Commercial activity</h2>
      {monitor.isPending ? <LoadingState /> : monitor.isError ? <ErrorState>{errorMessage(monitor.error)}</ErrorState> : <>
        <h3>Subscriber subscriptions and wallets</h3>
        <div className="billing-table" role="region" aria-label="Subscriber subscriptions and wallets" tabIndex={0}><Table>
          <thead><tr>{['Subscriber', 'Plan', 'Status', 'Period ends', 'Wallet credits'].map(item => <th key={item} scope="col">{item}</th>)}</tr></thead>
          <tbody>{monitor.data.subscriptions.map(item => <tr key={item.subscriberId}><td>{item.name}</td><td>{item.plan}</td><td>{item.status}</td>
            <td>{date(item.periodEnd)}</td><td>{monitor.data.wallets.find(wallet => wallet.subscriberId === item.subscriberId)?.available ?? '—'}</td></tr>)}</tbody>
        </Table></div>
        {monitor.data.wallets.some(wallet => wallet.projectionMismatch) && <ErrorState>A wallet projection needs reconciliation.</ErrorState>}
        <h3>Payments and renewal attempts</h3>
        <div className="billing-table" role="region" aria-label="Payments and renewal attempts" tabIndex={0}><Table>
          <thead><tr>{['Reference', 'Purpose', 'Amount', 'Status', 'Exception'].map(item => <th key={item} scope="col">{item}</th>)}</tr></thead>
          <tbody>{monitor.data.payments.map(item => <tr key={item.id}><td>{item.providerPaymentId}</td><td>{item.purpose}</td>
            <td><Money amount={item.amount} /></td><td>{item.status}</td><td>{item.failureReason ?? '—'}</td></tr>)}</tbody>
        </Table></div>
        {monitor.data.total > 25 && <div className="access-actions">
          <Button variant="light" disabled={page === 1} onClick={() => setPage(page - 1)}>Previous page</Button>
          <span>Page {page}</span><Button variant="light" disabled={page * 25 >= monitor.data.total} onClick={() => setPage(page + 1)}>Next page</Button>
        </div>}
      </>}
    </Card>}
    {access.data.canMonitor && access.data.canAdjust && monitor.data && <WalletAdjustmentForm subscribers={monitor.data.subscriptions} />}
  </section>
}
function WalletAdjustmentForm({ subscribers }: { subscribers: Monitor['subscriptions'] }) {
  const client = useQueryClient()
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  const [complete, setComplete] = useState(false)
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const element = event.currentTarget
    const form = new FormData(element)
    setPending(true); setError(''); setComplete(false)
    try {
      await api('/vendor/billing/wallets/' + form.get('subscriber') + '/adjustments', 'POST',
        { quantity: num(form, 'quantity'), reference: form.get('reference'), reason: form.get('reason') })
      element.reset()
      await client.invalidateQueries({ queryKey: ['vendor-commercial', 'monitor'] })
      setComplete(true)
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setPending(false) }
  }
  return <Card className="workspace-card billing-card"><h2>Authorize additional credits</h2>
    <form onSubmit={event => void submit(event)}><fieldset disabled={pending || !subscribers.length} className="billing-fields">
      <Field id="adjust-subscriber" label="Subscriber"><Select id="adjust-subscriber" name="subscriber" required>
        <option value="">Select a subscriber</option>{subscribers.map(x => <option key={x.subscriberId} value={x.subscriberId}>{x.name} · {x.subscriberId.slice(0, 8)}</option>)}
      </Select></Field>
      <Field id="adjust-quantity" label="Additional credits"><Input id="adjust-quantity" name="quantity" type="number" min="1" max="100000" required /></Field>
      <Field id="adjust-reference" label="Approval reference"><Input id="adjust-reference" name="reference" maxLength={128} required /></Field>
      <Field id="adjust-reason" label="Reason"><Input id="adjust-reason" name="reason" maxLength={500} required /></Field>
      <Button type="submit">Record adjustment</Button>
    </fieldset></form>
    {complete && <p role="status">The adjustment is recorded in the credit ledger.</p>}
    {error && <ErrorState>{error}</ErrorState>}
  </Card>
}

function VersionTable({ items }: { items: Version[] }) {
  return <div className="billing-table" role="region" aria-label="Version history" tabIndex={0}><Table>
    <thead><tr>{['Code', 'Name', 'Version', 'Active', 'Effective date'].map(item => <th key={item} scope="col">{item}</th>)}</tr></thead>
    <tbody>{items.map(item => <tr key={item.id}><td>{item.code}</td><td>{item.name}</td><td>{item.version}</td>
      <td>{item.active ? 'Yes' : 'No'}</td><td>{date(item.effectiveAt)}</td></tr>)}</tbody>
  </Table></div>
}

function VersionForm({ kind, versions }: { kind: 'plans'; versions: PlanVersion[] } | { kind: 'packages'; versions: PackageVersion[] }) {
  const client = useQueryClient()
  const [selected, choose] = useState('')
  const [reset, setReset] = useState(0)
  const [pending, setPending] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const item = versions.find(version => version.id === selected)
  const plan = item && 'monthlyPrice' in item ? item : undefined
  const pkg = item && 'price' in item ? item : undefined
  const title = kind === 'plans' ? 'plan' : 'package'
  const id = 'vendor-' + kind + '-'
  const text = (name: string, title: string, value?: string, required = true) => <Field id={id + name} label={title}>
    <Input id={id + name} name={name} defaultValue={value} required={required} /></Field>
  const number = (name: string, title: string, value?: number | null, min = 0, step = '1', required = true) => <Field id={id + name} label={title}>
    <Input id={id + name} name={name} type="number" min={min} step={step} defaultValue={value ?? undefined} required={required} /></Field>
  const check = (name: string, title: string, value: boolean) => <label className="acknowledgement">
    <input type="checkbox" name={name} defaultChecked={value} />{title}</label>

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    setPending(true)
    setError('')
    setMessage('')
    try {
      const effective = String(form.get('effectiveAt'))
      const effectiveAt = effective ? new Date(effective + (effective.length === 16 ? ':00' : '') + '+08:00').toISOString() : new Date().toISOString()
      const common = { code: form.get('code'), name: form.get('name'), active: flag(form, 'active'), effectiveAt }
      const data = kind === 'plans' ? { ...common, description: form.get('description'),
        monthlyPrice: money(form, 'monthlyPrice'), annualPrice: money(form, 'annualPrice'), monthlyCredits: num(form, 'monthlyCredits'),
        annualCredits: num(form, 'annualCredits'), allocationFrequency: form.get('allocationFrequency'), companyLimit: num(form, 'companyLimit'),
        userLimit: num(form, 'userLimit'), connectorLimit: num(form, 'connectorLimit'), graceDays: num(form, 'graceDays'),
        creditValidityDays: days(form, 'creditValidityDays'), integrations: list(form, 'integrations'), connectorCategories: list(form, 'connectorCategories'),
        features: list(form, 'features'), rollover: flag(form, 'rollover'), renewalAllowed: flag(form, 'renewalAllowed'),
        autoTopUpAllowed: flag(form, 'autoTopUpAllowed') } : { ...common, credits: num(form, 'credits'), price: money(form, 'price'),
        validityDays: days(form, 'validityDays'), eligiblePlans: list(form, 'eligiblePlans') }
      const saved = await api<Version>('/vendor/billing/' + kind, 'POST', data)
      await Promise.all([client.invalidateQueries({ queryKey: catalogKey }), client.invalidateQueries({ queryKey: ['billing-catalog'] })])
      choose(saved.id)
      setReset(previous => previous + 1)
      setMessage(saved.name + ' version ' + saved.version + ' saved.')
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setPending(false) }
  }
  return <Card className="workspace-card billing-card">
    <h2>Publish {title} version</h2>
    <Field id={id + 'copy'} label={'Copy a ' + title + ' version'}><Select id={id + 'copy'} value={selected} disabled={pending} onChange={event => choose(event.target.value)}>
      <option value="">New offering</option>{versions.map(version => <option value={version.id} key={version.id}>{version.name} · v{version.version}</option>)}
    </Select></Field>
    <form key={selected + reset} onSubmit={event => void save(event)}><fieldset className="billing-fields" disabled={pending}>
      {text('code', 'Code', item?.code)}
      {text('name', 'Name', item?.name)}
      <Field id={id + 'effective'} label="Effective date and time (Manila)"><Input id={id + 'effective'} name="effectiveAt" type="datetime-local" /></Field>
      {kind === 'plans' ? <>
        {text('description', 'Description', plan?.description)}
        {number('monthlyPrice', 'Monthly price (PHP)', plan?.monthlyPrice, .01, '.01')}
        {number('annualPrice', 'Annual price (PHP)', plan?.annualPrice, .01, '.01')}
        {number('monthlyCredits', 'Monthly credits', plan?.monthlyCredits)}
        {number('annualCredits', 'Annual credits', plan?.annualCredits)}
        <Field id={id + 'allocation'} label="Allocation schedule"><Select id={id + 'allocation'} name="allocationFrequency" defaultValue={plan?.allocationFrequency ?? 'period'}>
          <option value="period">At start of paid period</option><option value="monthly">Monthly within a paid period</option>
        </Select></Field>
        {number('companyLimit', 'Maximum company files', plan?.companyLimit, 1)}
        {number('userLimit', 'Maximum users', plan?.userLimit, 1)}
        {number('connectorLimit', 'Maximum connectors', plan?.connectorLimit)}
        {number('creditValidityDays', 'Credit validity in days', plan?.creditValidityDays, 1, '1', false)}
        {number('graceDays', 'Grace period in days', plan?.graceDays ?? 0)}
        {text('integrations', 'Connector codes (comma-separated)', plan?.integrations.join(', '), false)}
        {text('connectorCategories', 'Connector categories (comma-separated)', plan?.connectorCategories.join(', '), false)}
        {text('features', 'Features (comma-separated)', plan?.features.join(', '), false)}
        {check('rollover', 'Allow rollover', plan?.rollover ?? false)}
        {check('renewalAllowed', 'Allow renewals', plan?.renewalAllowed ?? true)}
        {check('autoTopUpAllowed', 'Allow automatic top-up consent', plan?.autoTopUpAllowed ?? false)}
      </> : <>
        {number('credits', 'Save & Send Credits', pkg?.credits, 1)}
        {number('price', 'Price (PHP)', pkg?.price, .01, '.01')}
        {number('validityDays', 'Validity in days', pkg?.validityDays, 1, '1', false)}
        {text('eligiblePlans', 'Eligible plan codes (comma-separated)', pkg?.eligiblePlans.join(', '), false)}
      </>}
      {check('active', 'Publish when effective', item?.active ?? true)}
      <Button type="submit">Save {title} version</Button>
    </fieldset></form>
    {message && <p role="status">{message}</p>}{error && <ErrorState>{error}</ErrorState>}
  </Card>
}

