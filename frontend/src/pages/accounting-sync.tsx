import { useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { Badge, Button, Card, ErrorState, Field, Input, LoadingState, Select } from '@/components/ui'
import { api, errorMessage, manilaDate, manilaTimestamp, type Branch } from '@/lib/account-api'
import { connectRoot, type Connection } from '@/lib/connect-api'
import { documentKinds, documentKindLabels, pretty, type AccountingJob, type Destination, type DocumentKind, type JobDetails, type MappingProfile } from '@/lib/mapping-api'
import { ScrollTable } from './connect'
import { useConnectAction } from './field-mapping'

const root = connectRoot + '/synchronization'
type DestinationWindow = { id: string; documentType: DocumentKind; branchId: string | null; connection: string;
  start: string; end: string | null; approvedAt: string; closedAt: string | null; automatic: boolean; reason: string }

const activeStatuses = ['Queued', 'Processing', 'Submitted', 'AwaitingConfirmation', 'RetryScheduled']
const statuses = ['NotApplicable', 'NotConfigured', 'AwaitingConnection', 'AwaitingMapping', ...activeStatuses,
  'Synchronized', 'Failed', 'RequiresReview', 'UncertainExternalOutcome']
const formatted = (value: string) => manilaTimestamp.format(new Date(value))
const tone = (status: string) => status === 'Synchronized' ? 'success' : status === 'RequiresReview' || status === 'UncertainExternalOutcome' ? 'warning' : 'neutral'

export function Synchronization({ base, id, branches }: { base: string; id?: string; branches: Branch[] }) {
  if (id) return <SyncJobPanel base={base} id={id} inCenter />
  return <div className="connect-stack"><SyncRegister base={base} branches={branches} /><DestinationSettings base={base} branches={branches} /></div>
}

function SyncRegister({ base, branches }: { base: string; branches: Branch[] }) {
  const [query, setQuery] = useState({ q: '', status: '', type: '', branch: '', connection: '', from: '', through: '' })
  const [page, setPage] = useState(1)
  const requests = new URLSearchParams(Object.entries({ ...query, page: String(page) }).filter(([, v]) => v))
  const rules = useQuery({ queryKey: ['accounting-sync', base, 'jobs', requests.toString()], queryFn: () =>
    api<{ items: AccountingJob[]; latest?: string; total: number; pageSize: number }>(base + '/accounting/jobs?' + requests),
    refetchInterval: 3500, retry: false })
  const links = useQuery({ queryKey: ['connect', base, 'connections'], queryFn: () => api<{ items: Connection[] }>(base + '/connections'), retry: false })
  function change(key: keyof typeof query, value: string) { setPage(1); setQuery(old => ({ ...old, [key]: value })) }
  return <Card className="workspace-card connect-stack"><h2>Accounting synchronization queue</h2>
    <p>Last successful synchronization {rules.data?.latest ? formatted(rules.data.latest) : '—'}</p>
    <div className="connect-grid">
      <Field id="sync-search" label="Search document number"><Input id="sync-search" type="search" value={query.q} onChange={e => change('q', e.target.value)} /></Field>
      <Field id="sync-status" label="Synchronization status"><Select id="sync-status" value={query.status} onChange={e => change('status', e.target.value)}>
        <option value="">All statuses</option>{statuses.map(s => <option key={s} value={s}>{pretty(s)}</option>)}</Select></Field>
      <Field id="sync-type" label="Document type"><Select id="sync-type" value={query.type} onChange={e => change('type', e.target.value)}>
        <option value="">All document types</option>{documentKinds.map(v => <option key={v} value={v}>{documentKindLabels[v]}</option>)}</Select></Field>
      <Field id="sync-branch" label="Branch"><Select id="sync-branch" value={query.branch} onChange={e => change('branch', e.target.value)}>
        <option value="">All branches</option>{branches.map(v => <option key={v.id} value={v.id}>{v.name}</option>)}</Select></Field>
      <Field id="sync-connection" label="Destination"><Select id="sync-connection" value={query.connection} onChange={e => change('connection', e.target.value)}>
        <option value="">All destinations</option>{links.data?.items.filter(c => c.resourceType === 'CompanyFile').map(v =>
          <option key={v.id} value={v.id}>{v.name}</option>)}</Select></Field>
      <Field id="sync-from" label="Document date from"><Input id="sync-from" type="date" value={query.from} onChange={e => change('from', e.target.value)} /></Field>
      <Field id="sync-through" label="Document date through"><Input id="sync-through" type="date" value={query.through} onChange={e => change('through', e.target.value)} /></Field>
    </div>
    {rules.isPending ? <LoadingState /> : rules.isError ? <ErrorState>{errorMessage(rules.error)}</ErrorState> :
      rules.data.items.length ? <>
        <ScrollTable name="Synchronization jobs"><thead><tr><th>Document</th><th>Date</th><th>Status</th><th>Destination</th><th>External reference</th></tr></thead>
          <tbody>{rules.data.items.map(row => <tr key={row.id}><td><Link className="text-link" to={root + '/' + row.id}>{row.number}</Link>
            <small className="mapping-hint">{documentKindLabels[row.type]}</small></td><td>{row.date}</td>
            <td><Badge tone={tone(row.status)}>{pretty(row.status)}</Badge></td><td>{links.data?.items.find(x => x.id === row.connectionId)?.name ?? '—'}</td>
            <td>{row.externalNumber ?? '—'}</td></tr>)}</tbody>
        </ScrollTable><div className="connect-actions"><Button variant="light" disabled={page === 1} onClick={() => setPage(page - 1)}>Previous</Button>
          <span>Page {page}</span><Button variant="light" disabled={page * rules.data.pageSize >= rules.data.total} onClick={() => setPage(page + 1)}>Next</Button></div>
      </> : <p>No documents match these filters.</p>}
  </Card>
}

function DestinationSettings({ base, branches }: { base: string; branches: Branch[] }) {
  const [type, setType] = useState<DocumentKind>('SalesInvoice')
  const [branch, setBranch] = useState('')
  const [profile, setProfile] = useState('')
  const [enabled, setEnabled] = useState(true)
  const [automatic, setAutomatic] = useState(true)
  const [scenario, setScenario] = useState('Normal')
  const [window, setWindow] = useState({ start: '', end: '', reason: '', confirm: false, closeWindowId: '' })
  const cache = useQueryClient()
  const actions = useConnectAction()
  const path = base + '/accounting/destinations'
  const routes = useQuery({ queryKey: ['accounting-sync', base, 'destinations'], queryFn: () => api<{ routes: Destination[]; windows: DestinationWindow[]; scenarios: string[] }>(path), retry: false })
  const options = useQuery({ queryKey: ['field-mapping', base], queryFn: () => api<{ items: MappingProfile[] }>(base + '/mapping-profiles'), retry: false })
  if (routes.isPending || options.isPending) return <LoadingState label="Loading destinations…" />
  if (routes.isError || options.isError) return <ErrorState>{errorMessage(routes.error ?? options.error)}</ErrorState>
  function save(event: FormEvent) {
    event.preventDefault()
    void actions.run(async () => {
      const scope = { documentType: type, branchId: branch || null, profileId: profile, automatic }
      if (window.start) {
        await api(base + '/accounting/destination-windows', 'POST', {
          ...scope, ...window, end: window.end || null, closeWindowId: window.closeWindowId || null,
        })
        setWindow({ start: '', end: '', reason: '', confirm: false, closeWindowId: '' })
      } else {
        await api(path, 'PUT', { ...scope, enabled, scenario })
      }
      await cache.invalidateQueries({ queryKey: ['accounting-sync', base] })
    }, window.start ? 'Destination window approved' : 'Accounting destination saved')
  }
  return <Card className="workspace-card connect-stack"><h2>Accounting destinations</h2>
    <p>Choose one destination for each document type and branch scope. A branch rule takes precedence over the company default.</p>
    {routes.data.routes.length > 0 && <ScrollTable name="Accounting destinations"><thead><tr><th>Document type</th><th>Branch</th><th>External company</th><th>Health</th><th>Policy</th></tr></thead>
      <tbody>{routes.data.routes.map(route => <tr key={route.id}><td>{documentKindLabels[route.documentType]}</td>
        <td>{branches.find(x => x.id === route.branchId)?.name ?? 'Company-wide'}</td><td>{route.externalResourceName}</td><td>{route.health}</td>
        <td>{route.enabled ? route.automatic ? 'Automatic' : 'Manual' : 'Disabled'} <Button type="button" variant="light" onClick={() => {
          setType(route.documentType); setBranch(route.branchId ?? ''); setProfile(route.profileId); setEnabled(route.enabled); setAutomatic(route.automatic); setScenario(route.scenario)
        }}>Edit</Button></td></tr>)}</tbody></ScrollTable>}
    {routes.data.windows.length > 0 && <ScrollTable name="Accounting destination history"><thead><tr>
      <th>Document type</th><th>Branch</th><th>Destination</th><th>From</th><th>Through</th><th>State</th><th>Approval reason</th>
    </tr></thead><tbody>{routes.data.windows.map(row => <tr key={row.id}>
      <td>{documentKindLabels[row.documentType]}</td><td>{branches.find(x => x.id === row.branchId)?.name ?? 'Company-wide'}</td>
      <td>{row.connection}</td><td>{row.start}</td><td>{row.end ?? 'Open-ended'}</td>
      <td>{row.start > manilaDate() ? 'Future' : row.end && row.end < manilaDate() ? 'Previous' : 'Active'}</td><td>{row.reason}</td>
    </tr>)}</tbody></ScrollTable>}
    <form className="connect-stack" onSubmit={save}><div className="connect-grid">
      <Field id="destination-type" label="Destination document type"><Select id="destination-type" value={type} onChange={e => { setType(e.target.value as DocumentKind); setProfile('') }}>
        {documentKinds.map(x => <option key={x} value={x}>{documentKindLabels[x]}</option>)}</Select></Field>
      <Field id="destination-branch" label="Destination branch"><Select id="destination-branch" value={branch} onChange={e => setBranch(e.target.value)}>
        <option value="">Company-wide</option>{branches.map(x => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}</Select></Field>
      <Field id="destination-profile" label="Outbound mapping profile"><Select id="destination-profile" required value={profile} onChange={e => setProfile(e.target.value)}>
        <option value="">Choose a profile</option>{options.data.items.filter(x => x.direction === 'Outbound' && x.documentType === type).map(x =>
          <option key={x.id} value={x.id}>{x.name} · {x.connectionName}</option>)}</Select></Field>
      <Field id="destination-start" label="Destination start date (optional)"><Input id="destination-start" type="date"
        value={window.start} onChange={e => setWindow({ ...window, start: e.target.value })} /></Field>
      {window.start ? <>
        <Field id="destination-end" label="Destination end date (optional)"><Input id="destination-end" type="date" min={window.start}
          value={window.end} onChange={e => setWindow({ ...window, end: e.target.value })} /></Field>
        <Field id="destination-close" label="Open window to close (optional)"><Select id="destination-close" value={window.closeWindowId}
          onChange={e => setWindow({ ...window, closeWindowId: e.target.value })}><option value="">Keep existing windows</option>
          {routes.data.windows.filter(x => !x.end && x.documentType === type && (x.branchId ?? '') === branch).map(x =>
            <option key={x.id} value={x.id}>{x.connection} · {x.start}</option>)}
        </Select></Field>
        <Field id="destination-reason" label="Destination change reason"><Input id="destination-reason" required maxLength={300}
          value={window.reason} onChange={e => setWindow({ ...window, reason: e.target.value })} /></Field>
        <label className="connect-check"><input type="checkbox" required checked={window.confirm}
          onChange={e => setWindow({ ...window, confirm: e.target.checked })} /> Approve inclusive destination window</label>
      </> : <>
      <Field id="destination-scenario" label="Development synchronization scenario"><Select id="destination-scenario" value={scenario} onChange={e => setScenario(e.target.value)}>
        {routes.data.scenarios.map(x => <option key={x} value={x}>{pretty(x)}</option>)}</Select></Field>
      <label className="connect-check"><input type="checkbox" checked={enabled} onChange={e => setEnabled(e.target.checked)} /> Enable destination</label>
      </>}
      <label className="connect-check"><input type="checkbox" checked={automatic} onChange={e => setAutomatic(e.target.checked)} /> Synchronize automatically</label>
    </div>{actions.error && <ErrorState>{actions.error}</ErrorState>}{actions.notice && <p role="status">{actions.notice}</p>}
      <div className="connect-actions"><Button type="submit" disabled={!profile || actions.busy}>{window.start ? 'Approve destination window' : 'Save accounting destination'}</Button>
        <Link className="text-link" to={connectRoot + '/field-mapping'}>Open Field Mapping</Link></div>
    </form>
  </Card>
}

export function SyncJobPanel({ base, id, inCenter = false }: { base: string; id: string; inCenter?: boolean }) {
  const cache = useQueryClient()
  const actions = useConnectAction()
  const [replacement, setReplacement] = useState('')
  const path = base + '/accounting/jobs/' + id
  const job = useQuery({ queryKey: ['accounting-sync', base, 'job', id], queryFn: () => api<JobDetails>(path), retry: false,
    refetchInterval: query => query.state.data && activeStatuses.includes(query.state.data.status) ? 2000 : false })
  if (job.isPending) return inCenter ? <LoadingState label="Loading job history…" /> : null
  if (job.isError) return inCenter ? <ErrorState>{errorMessage(job.error)}</ErrorState> : null
  const detail = job.data
  const manageable = detail.canManage && !activeStatuses.includes(detail.status) && detail.status !== 'Synchronized'
  function queue(mode: 'verify' | 'retry') {
    void actions.run(async () => {
      await api(path + '/retry', 'POST', { mode, replacementVersion: mode === 'retry' && replacement ? Number(replacement) : null })
      await cache.invalidateQueries({ queryKey: ['accounting-sync', base] })
    }, mode === 'verify' ? 'Verification queued' : 'Retry queued')
  }
  return <Card className="workspace-card connect-stack">
    {inCenter && <Link className="back-link" to={root}>Back to Synchronization Center</Link>}
    <div className="connect-actions"><h2>{inCenter ? detail.number : 'Accounting synchronization'}</h2>
      <Badge tone={tone(detail.status)}>{pretty(detail.status)}</Badge></div>
    {inCenter && <Link className="text-link" to={'/portal/client/documents/' + detail.type + '/' + detail.id}>Open permanent record</Link>}
    {detail.identity && <dl className="workspace-definitions">
      <div><dt>eFiscal number</dt><dd>{detail.identity.internalNumber}</dd></div>
      <div><dt>External accounting number</dt><dd>{detail.identity.externalNumber}</dd></div>
      <div><dt>External record ID</dt><dd>{detail.identity.externalId}</dd></div>
      <div><dt>External company file</dt><dd>{detail.identity.externalCompany}</dd></div>
      <div><dt>Confirmed</dt><dd>{formatted(detail.identity.confirmedAt)}</dd></div>
    </dl>}
    {!!detail.attempts.length && <ScrollTable name="Accounting synchronization attempts"><thead><tr><th>Attempt</th><th>Status</th><th>Mapping</th><th>Time</th><th>Details</th></tr></thead>
      <tbody>{detail.attempts.map(a => <tr key={a.id}><td>{a.number}</td><td>{pretty(a.status)}</td><td>v{a.mappingNumber}</td>
        <td>{formatted(a.startedAt)}</td><td>{a.category ? pretty(a.category) : a.evidence ?? '—'}</td></tr>)}</tbody></ScrollTable>}
    {manageable && <div className="connect-actions">
      {detail.profileId && <Link className="text-link" to={connectRoot + '/field-mapping/' + detail.profileId}>Review pinned mapping</Link>}
      {detail.canRetry && detail.profileId && <Field id={'retry-map-' + id} label="Replacement mapping version (optional)">
        <Input id={'retry-map-' + id} value={replacement} type="number" min="1" step="1" onChange={e => setReplacement(e.target.value)} /></Field>}
      {detail.canRetry && <Button disabled={actions.busy} variant="light" onClick={() => queue('retry')}>Retry synchronization</Button>}
      {detail.canVerify && <Button disabled={actions.busy} variant="light" onClick={() => queue('verify')}>Verify external outcome</Button>}
    </div>}
    {actions.error && <ErrorState>{actions.error}</ErrorState>}{actions.notice && <p role="status">{actions.notice}</p>}
  </Card>
}

