import { useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, NavLink } from 'react-router-dom'
import { Badge, Button, Card, ErrorState, Field, Input, LoadingState, Select } from '@/components/ui'
import { api, errorMessage, manilaTimestamp, type Branch } from '@/lib/account-api'
import { invoiceFromSource } from '@/lib/connect-api'
import { reconciliationRoot, coverageRoot, status, typeNames, type AccountingLink, type Comparison, type CoverageList, type DocumentReconciliation,
  type Finding, type ReconciliationOverview, type ReconcileCheck, type ReconcileJob, type ReconcileType, type ReferenceRow, type References } from '@/lib/reconciliation-api'
import { ScrollTable } from './connect'
import { useConnectAction } from './field-mapping'
import './reconciliation.css'

const tabs = [
  ['overview', 'Overview'], ['register', 'Reconciliation Register'], ['discrepancies', 'Discrepancies'],
  ['missing-accounting', 'Missing Accounting Records'], ['missing-efiscal', 'Missing eFiscal Records'], ['duplicates', 'Duplicate Records'],
  ['history', 'Reconciliation History'], ['reports', 'Reconciliation Reports'], ['references', 'Historical References'],
]
const statuses = ['Matched', 'MatchedWithinTolerance', 'PartialVerification', 'AwaitingSynchronization', 'MissingAccountingRecord', 'MissingEfiscalRecord',
  'DuplicateExternalRecords', 'HeaderMismatch', 'LineMismatch', 'TaxMismatch', 'DiscountMismatch', 'FinancialMismatch', 'RelationshipMismatch',
  'AmbiguousMatch', 'HistoricalExcluded', 'OutsideCoveragePeriod', 'NotExpected', 'IncompleteRetrieval', 'ConnectionUnavailable', 'StaleVerification']
const defaultStatus: Record<string, string> = { 'missing-accounting': 'MissingAccountingRecord', 'missing-efiscal': 'MissingEfiscalRecord', duplicates: 'DuplicateExternalRecords' }
const readInterval = 3500
type SchemaList<T> = { items: T[]; hasMore?: boolean }
type Grants = { canRun: boolean; canExport: boolean; canReference: boolean; canManage: boolean }
type Filter = { connection: string; branch: string; type: string; status: string; category: string; coverageMode: string; search: string; from: string; through: string; sort: string }

export function ReconciliationWorkspace({ base, section, branches, selectedBranch, permissions }: { base: string; section: string; branches: Branch[];
  selectedBranch?: string; permissions: string[] }) {
  const [page, checkId] = section.split('/')
  const grants = { canRun: permissions.includes('Reconciliation.BatchRun'), canExport: permissions.includes('Reconciliation.Export'),
    canReference: permissions.includes('Reconciliation.ViewHistoricalReferences'), canManage: permissions.includes('Reconciliation.ManageCoverage') }
  return <div className="connect-stack">
    <nav className="reconciliation-nav" aria-label="Reconciliation workspace">
      {tabs.filter(([route]) => route !== 'references' || grants.canReference).map(([route, label]) =>
        <NavLink className="text-link" key={route} to={reconciliationRoot + '/' + route}>{label}</NavLink>)}
      {grants.canManage && <Link className="text-link" to={coverageRoot}>Coverage Configuration</Link>}
    </nav>
    {page === 'comparison' && checkId ? <ComparisonPanel base={base} id={checkId} />
      : page === 'discrepancies' ? <Findings base={base} />
        : page === 'references' && grants.canReference ? <ReferencePanel base={base} />
          : <Registers key={page + ':' + selectedBranch} base={base} page={page} branches={branches} selectedBranch={selectedBranch} grants={grants} />}
  </div>
}

function Registers({ base, page: view, branches, selectedBranch, grants }: { base: string; page: string; branches: Branch[]; selectedBranch?: string; grants: Grants }) {
  const [filter, setFilter] = useState<Filter>({ connection: '', branch: selectedBranch ?? '', type: '', status: defaultStatus[view] ?? '',
    category: '', coverageMode: '', search: '', from: '', through: '', sort: '' })
  const [page, setPage] = useState(1)
  const [report, setReport] = useState('summary')
  const summary = !view || view === 'overview'
  const reports = view === 'reports'
  const coverage = useQuery({ queryKey: ['reconciliation', base, 'coverage'], queryFn: () => api<CoverageList>(base + '/coverage'), retry: false })
  const params = new URLSearchParams()
  Object.entries(filter).forEach(([name, value]) => { if (value) params.set(name, value) })
  if (view === 'history') params.set('history', 'true')
  const reportStatuses = { 'Missing Accounting Records': 'MissingAccountingRecord', 'Missing eFiscal Records': 'MissingEfiscalRecord',
    'Duplicate Accounting Records': 'DuplicateExternalRecords', 'Historical Exclusions': 'HistoricalExcluded' } as Record<string, string>
  const reportCategories = { 'Tax Differences': 'TaxMismatch', 'Discount Differences': 'DiscountMismatch', 'Line-Item Differences': 'LineMismatch',
    'Financial Discrepancies': 'FinancialMismatch', 'Document Relationships': 'RelationshipMismatch' } as Record<string, string>
  if (reports) {
    if (reportStatuses[report]) params.set('status', reportStatuses[report])
    if (reportCategories[report]) params.set('category', reportCategories[report])
    if (report === 'Reconciliation History') params.set('history', 'true')
  }
  const search = params.toString()
  const request = search + '&page=' + page
  const rows = useQuery({ queryKey: ['reconciliation', base, 'register', request], queryFn: () => api<SchemaList<ReconcileCheck>>(base + '/register?' + request),
    retry: false, refetchInterval: readInterval })
  const stats = useQuery({ queryKey: ['reconciliation', base, 'overview', search], queryFn: () => api<ReconciliationOverview>(base + '/overview?' + search),
    retry: false, enabled: summary, refetchInterval: readInterval })
  const update = (name: keyof Filter, value: string) => { setPage(1); setFilter(old => ({ ...old, [name]: value })) }
  const links = coverage.data?.links ?? []
  const active = (coverage.data?.items ?? []).filter(x => x.active && (!filter.connection || x.connectionId === filter.connection))
  const label = tabs.find(([route]) => route === view)?.[1] ?? 'Overview'
  return <>
    <Card className="workspace-card connect-stack">
      <h2>{summary ? 'Reconciliation overview' : label}</h2>
      <div className="connect-grid">
        <Field id="reconciliation-connection" label="Filter accounting connection"><Select id="reconciliation-connection" value={filter.connection} onChange={e => update('connection', e.target.value)}>
          <option value="">All accounting connections</option>{links.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field>
        <Field id="reconciliation-branch" label="Filter branch"><Select id="reconciliation-branch" value={filter.branch} onChange={e => update('branch', e.target.value)}>
          <option value="">All branches</option>{branches.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field>
        <Field id="reconciliation-type" label="Document type"><Select id="reconciliation-type" value={filter.type} onChange={e => update('type', e.target.value)}>
          <option value="">All document types</option>{Object.entries(typeNames).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</Select></Field>
        <Field id="reconciliation-status" label="Reconciliation status"><Select id="reconciliation-status" value={filter.status} onChange={e => update('status', e.target.value)}>
          <option value="">All statuses</option>{statuses.map(x => <option key={x} value={x}>{status(x)}</option>)}</Select></Field>
        <Field id="reconciliation-category" label="Discrepancy category"><Select id="reconciliation-category" value={filter.category} onChange={e => update('category', e.target.value)}>
          <option value="">All categories</option>{statuses.filter(x => x.includes('Mismatch') || x.startsWith('Missing') || x.startsWith('Duplicate')).map(x => <option key={x} value={x}>{status(x)}</option>)}</Select></Field>
        <Field id="reconciliation-mode" label="Filter coverage mode"><Select id="reconciliation-mode" value={filter.coverageMode} onChange={e => update('coverageMode', e.target.value)}>
          <option value="">All coverage modes</option><option value="EfiscalOnly">eFiscal-only</option><option value="FullCoverage">Full coverage</option></Select></Field>
        <Field id="reconciliation-search" label="Search document number"><Input id="reconciliation-search" type="search" maxLength={80} value={filter.search} onChange={e => update('search', e.target.value)} /></Field>
        <Field id="reconciliation-from" label="Business date from"><Input id="reconciliation-from" type="date" value={filter.from} onChange={e => update('from', e.target.value)} /></Field>
        <Field id="reconciliation-through" label="Business date through"><Input id="reconciliation-through" type="date" value={filter.through} onChange={e => update('through', e.target.value)} /></Field>
        <Field id="reconciliation-sort" label="Sort"><Select id="reconciliation-sort" value={filter.sort} onChange={e => update('sort', e.target.value)}>
          <option value="">Latest verification</option><option value="oldest">Earliest verification</option><option value="date">Business date</option></Select></Field>
      </div>
      {summary && <>
        {stats.isPending ? <LoadingState label="Loading verification statistics…" /> : stats.isError ? <ErrorState>{errorMessage(stats.error)}</ErrorState>
          : <OverviewMetrics stats={stats.data} />}
        {active.length > 0 && <ScrollTable name="Active coverage"><thead><tr><th>Connection</th><th>Coverage</th><th>Mode</th><th>Version</th></tr></thead>
          <tbody>{active.map(x => <tr key={x.id}><td>{links.find(c => c.id === x.connectionId)?.name}</td><td>{x.start} to {x.end ?? 'open'}</td>
            <td>{status(x.mode)}</td><td>v{x.number}</td></tr>)}</tbody></ScrollTable>}
      </>}
      {reports && <><Field id="reconciliation-report" label="Report view"><Select id="reconciliation-report" value={report} onChange={e => { setReport(e.target.value); setPage(1) }}>
        <option value="summary">Electronic Invoice Reconciliation Summary</option>
        {[...Object.keys(reportStatuses), ...Object.keys(reportCategories), 'Reconciliation History', 'Synchronization and Reconciliation Status'].map(x => <option key={x}>{x}</option>)}
      </Select></Field><p>Exports retain each record’s currency, coverage version, retrieval status, and verification timestamp.</p></>}
      {grants.canExport && <div className="connect-actions">
        <Button asChild variant="light"><a href={'/api' + base + '/reports/csv?' + search} download>Export CSV</a></Button>
        <Button asChild variant="light"><a href={'/api' + base + '/reports/xlsx?' + search} download>Export XLSX</a></Button>
      </div>}
    </Card>
    {summary && grants.canRun && <JobControls base={base} links={links} />}
    <Card className="workspace-card">
      {summary && <h2>Recent reconciliation results</h2>}
      {rows.isPending ? <LoadingState label="Loading reconciliation register…" /> : rows.isError ? <ErrorState>{errorMessage(rows.error)}</ErrorState>
        : <><CheckTable rows={rows.data.items} /><div className="connect-actions">
          <Button variant="light" disabled={page === 1} onClick={() => setPage(page - 1)}>Previous</Button><span>Page {page}</span>
          <Button variant="light" disabled={!rows.data.hasMore} onClick={() => setPage(page + 1)}>Next</Button></div></>}
    </Card>
  </>
}

function OverviewMetrics({ stats }: { stats: ReconciliationOverview }) {
  const values = [
    ['Eligible comparison units', stats.eligible], ['Conclusive outcomes', stats.conclusive], ['Exact matches', stats.exact],
    ['Within approved tolerance', stats.withinTolerance], ['Partial verification', stats.partial], ['Units with discrepancies', stats.discrepancyUnits],
    ['Individual findings', stats.findings], ['Missing accounting', stats.missingAccounting], ['Missing eFiscal', stats.missingEfiscal],
    ['Duplicates', stats.duplicates], ['Awaiting synchronization', stats.awaiting], ['Not yet verified', stats.notYetVerified], ['Stale evidence', stats.stale],
    ['Observed historical exclusions', stats.observedHistorical ?? 'Unavailable'], ['Observed outside coverage', stats.observedOutside],
    ['Exact match rate', percentage(stats.matchRate)], ['Verification coverage', percentage(stats.verificationCoverage)],
  ] as const
  return <><dl className="connect-grid reconciliation-metrics">{values.map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>
    <p>Exact match rate uses conclusive eligible outcomes. Verification coverage uses all eligible comparison units. Historical and out-of-scope records are excluded from both rates.</p>
    {stats.latest && <p>Latest successful verification {manilaTimestamp.format(new Date(stats.latest))}</p>}</>
}

function percentage(number?: number | null) { return number == null ? 'Unavailable' : new Intl.NumberFormat('en-PH', { style: 'percent', maximumFractionDigits: 1 }).format(number) }

function CheckTable({ rows }: { rows: ReconcileCheck[] }) {
  return rows.length ? <ScrollTable name="Reconciliation results"><thead><tr>{['eFiscal', 'Accounting', 'Type', 'Business date', 'eFiscal total', 'Accounting total', 'Coverage', 'Reconciliation', 'Verification'].map(x => <th key={x}>{x}</th>)}</tr></thead>
    <tbody>{rows.map(x => <tr key={x.id}><td>{x.number ?? '—'}</td><td>{x.externalNumber ?? '—'}</td><td>{typeNames[x.type]}</td><td>{x.businessDate ?? '—'}</td>
      <td>{amount(x.expectedTotal, x.currency)}</td><td>{amount(x.actualTotal, x.currency)}</td><td>{status(x.coverage)}</td>
      <td><div>{status(x.status)}{x.stale && <Badge tone="warning">Stale evidence</Badge>}</div><Link className="text-link" to={reconciliationRoot + '/comparison/' + x.id}>View comparison</Link></td>
      <td>{status(x.completeness)}<br />{manilaTimestamp.format(new Date(x.at))}</td></tr>)}</tbody></ScrollTable> : <p>No results match these filters.</p>
}

function amount(value?: number | null, currency?: string) { return value == null ? '—' : [currency, value].filter(x => x != null).join(' ') }

function JobControls({ base, links }: { base: string; links: AccountingLink[] }) {
  const jobs = useQuery({ queryKey: ['reconciliation', base, 'jobs'], queryFn: () => api<SchemaList<ReconcileJob>>(base + '/jobs'), retry: false, refetchInterval: readInterval })
  const cache = useQueryClient()
  const action = useConnectAction()
  const [kind, setKind] = useState('Batch')
  function queue(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const values = new FormData(e.currentTarget)
    void action.run(async () => {
      await api(base + '/jobs', 'POST', { kind, connectionId: values.get('connection'), from: values.get('from') || null,
        through: values.get('through') || null, key: crypto.randomUUID() })
      await cache.invalidateQueries({ queryKey: ['reconciliation', base] })
    }, 'Reconciliation job queued')
  }
  return <Card className="workspace-card connect-stack"><h2>Reconciliation jobs</h2>
    <form className="connect-stack" onSubmit={queue}><div className="connect-grid">
      <Field id="reconciliation-job-connection" label="Job accounting connection"><Select id="reconciliation-job-connection" name="connection" required>
        <option value="">Choose a connection</option>{links.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field>
      <Field id="reconciliation-job-kind" label="Job type"><Select id="reconciliation-job-kind" value={kind} onChange={e => setKind(e.target.value)}>
        <option value="Batch">Committed documents</option><option value="Discovery">Accounting discovery</option></Select></Field>
      <Field id="reconciliation-job-from" label="Job period start"><Input id="reconciliation-job-from" name="from" type="date" required={kind === 'Discovery'} /></Field>
      <Field id="reconciliation-job-through" label="Job period end"><Input id="reconciliation-job-through" name="through" type="date" required={kind === 'Discovery'} /></Field>
    </div><div><Button type="submit" disabled={action.busy || !links.length}>Queue reconciliation</Button></div></form>
    {action.error && <ErrorState>{action.error}</ErrorState>}{action.notice && <p role="status">{action.notice}</p>}
    {jobs.isError && <ErrorState>{errorMessage(jobs.error)}</ErrorState>}
    {!!jobs.data?.items.length && <ScrollTable name="Reconciliation job history"><thead><tr><th>Job</th><th>State</th><th>Started</th><th>Details</th></tr></thead>
      <tbody>{jobs.data.items.slice(0, 15).map(j => <tr key={j.id}><td>{status(j.kind)}</td><td>{status(j.state)}</td><td>{manilaTimestamp.format(new Date(j.createdAt))}</td>
        <td>{j.failure && status(j.failure)}{j.state === 'Queued' && j.attempts === 0 && <Button type="button" variant="light" disabled={action.busy}
          onClick={() => void action.run(async () => { await api(base + '/jobs/' + j.id + '/cancel', 'POST'); await jobs.refetch() }, 'Job cancelled')}>Cancel</Button>}</td>
      </tr>)}</tbody></ScrollTable>}
  </Card>
}

function ComparisonPanel({ base, id }: { base: string; id: string }) {
  const query = useQuery({ queryKey: ['reconciliation', base, id], queryFn: () => api<Comparison>(base + '/comparisons/' + id), retry: false, refetchInterval: readInterval })
  const [differencesOnly, setDifferencesOnly] = useState(false)
  const [scenario, setScenario] = useState('Exact')
  const action = useConnectAction()
  const cache = useQueryClient()
  if (query.isPending) return <LoadingState label="Loading comparison evidence…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  const { check, cells, snapshot, coverage, history, findings, canRun, canReview, reviewers, canSimulate } = query.data
  const rows = cells.filter(x => !differencesOnly || x.state !== 'Matched')
  return <>
    <Card className="workspace-card connect-stack"><h2>Side-by-side comparison</h2>
      <dl className="workspace-definitions">
        <div><dt>Original EMBM eFiscal document</dt><dd>{check.number ?? 'No corresponding eFiscal document'}</dd></div>
        <div><dt>Actual accounting record</dt><dd>{check.externalNumber ?? 'No accounting record in this verification'}</dd></div>
        <div><dt>Financial result</dt><dd>{status(check.status)}</dd></div><div><dt>Verification completeness</dt><dd>{status(check.completeness)}</dd></div>
        <div><dt>Coverage and comparison policy</dt><dd>{coverage ? 'v' + coverage.number + ' · ' + status(coverage.mode) : 'Default exact comparison'}</dd></div>
        {snapshot && <><div><dt>External identity</dt><dd>{snapshot.externalId}</dd></div>
          <div><dt>External account and file</dt><dd>{snapshot.account} · {snapshot.externalCompany}</dd></div>
          <div><dt>Retrieved</dt><dd>{manilaTimestamp.format(new Date(snapshot.retrievedAt))}</dd></div>
          <div><dt>Connector version</dt><dd>{snapshot.connectorVersionId}</dd></div>
          <div><dt>Response mapping</dt><dd>{snapshot.normalizationVersion ? 'v' + snapshot.normalizationVersion : 'Built-in development fields'}</dd></div>
          <div><dt>Evidence hash</dt><dd>{snapshot.hash}</dd></div></>}
      </dl>
      <div className="connect-actions">{check.stale && <Badge tone="warning">Stale verification</Badge>}
        {check.externalChanged && <Badge tone="warning">External record changed</Badge>}
        {check.documentId && canRun && <RunDocument base={base} id={check.documentId} />}</div>
      {check.documentId && canSimulate && <details><summary>Simulate an external record revision</summary>
        <div className="connect-actions"><Field id="external-revision" label="Development external record scenario"><Select id="external-revision" value={scenario} onChange={e => setScenario(e.target.value)}>
          {['Exact', 'Vat', 'Discount', 'Customer', 'Quantity', 'Price', 'TaxClass', 'MissingLine', 'ExtraLine', 'DuplicateLine', 'Reorder', 'Unsupported',
            'Missing', 'Duplicate', 'Unavailable', 'RateLimit', 'IncompletePagination'].map(x => <option key={x} value={x}>{status(x)}</option>)}
        </Select></Field><Button type="button" variant="light" disabled={action.busy} onClick={() => void action.run(async () => {
          await api(base + '/development/' + check.documentId + '/external-revision', 'POST', { scenario })
          await cache.invalidateQueries({ queryKey: ['reconciliation', base] })
        }, 'External fixture revision recorded')}>Apply development revision</Button></div>
        {action.error && <ErrorState>{action.error}</ErrorState>}{action.notice && <p role="status">{action.notice}</p>}
      </details>}
    </Card>
    {!!rows.length && <Card className="workspace-card connect-stack"><h2>Field and line comparison</h2>
      <label className="connect-check"><input type="checkbox" checked={differencesOnly} onChange={e => setDifferencesOnly(e.target.checked)} />Show differences and unverified fields</label>
      <ScrollTable name="Comparison fields"><thead><tr>{['Field', 'eFiscal', 'Accounting', 'Difference', 'Result'].map(x => <th key={x}>{x}</th>)}</tr></thead>
        <tbody>{rows.map(c => <tr key={(c.line ?? '') + ':' + c.field}><td>{status(c.field)}{c.line && <small> · {c.line.slice(0, 8)}</small>}</td>
          <td>{value(c.expected)}</td><td>{value(c.actual)}</td><td>{value(c.difference)}</td><td>{status(c.state)}</td></tr>)}</tbody></ScrollTable>
    </Card>}
    {findings.length > 0 && <Card className="workspace-card connect-stack"><h2>Discrepancy review</h2>{findings.map(f =>
      <FindingReview key={f.id} base={base} finding={f} canReview={canReview} reviewers={reviewers} />)}</Card>}
    <Card className="workspace-card"><h2>Previous checks</h2><CheckTable rows={history} /></Card>
  </>
}

function value(x: unknown): string { return x == null ? '—' : typeof x === 'object' ? JSON.stringify(x) : String(x) }

function Findings({ base }: { base: string }) {
  const [page, setPage] = useState(1)
  const query = useQuery({ queryKey: ['reconciliation', base, 'findings', page], queryFn: () => api<SchemaList<Finding>>(base + '/discrepancies?page=' + page), retry: false })
  if (query.isPending) return <LoadingState label="Loading discrepancy findings…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  return <Card className="workspace-card"><h2>Discrepancy register</h2>
    {query.data.items.length ? <ScrollTable name="Discrepancy findings"><thead><tr><th>Category</th><th>Field</th><th>Review state</th><th>Detected</th></tr></thead>
      <tbody>{query.data.items.map(f => <tr key={f.id}><td><Link className="text-link" to={reconciliationRoot + '/comparison/' + f.checkId}>{status(f.category)}</Link></td>
        <td>{f.field}</td><td>{status(f.status)}</td><td>{manilaTimestamp.format(new Date(f.detectedAt))}</td></tr>)}</tbody></ScrollTable> : <p>No discrepancies recorded.</p>}
    <div className="connect-actions"><Button variant="light" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</Button>
      <Button variant="light" disabled={!query.data.hasMore} onClick={() => setPage(page + 1)}>Next</Button></div>
  </Card>
}

function FindingReview({ base, finding, canReview, reviewers }: { base: string; finding: Finding; canReview: boolean; reviewers: Comparison['reviewers'] }) {
  const action = useConnectAction()
  const cache = useQueryClient()
  function save(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const values = new FormData(e.currentTarget)
    void action.run(async () => {
      await api(base + '/discrepancies/' + finding.id + '/review', 'POST', { status: values.get('status'), note: values.get('note'), reviewer: values.get('reviewer') || null })
      await cache.invalidateQueries({ queryKey: ['reconciliation', base] })
    }, 'Review event saved')
  }
  return <details className="connect-order"><summary>{status(finding.category)} · {finding.field} · {status(finding.status)}</summary>
    {finding.history.map(event => <p key={event.id}>{status(event.status)} · {manilaTimestamp.format(new Date(event.at))} {event.note && ' · ' + event.note}
      {event.verificationId && <> · <Link className="text-link" to={reconciliationRoot + '/comparison/' + event.verificationId}>Verification evidence</Link></>}</p>)}
    {canReview && finding.status !== 'ResolvedAfterVerification' && <form className="connect-stack" onSubmit={save}><div className="connect-grid">
      <Field id={'review-state-' + finding.id} label="Review status"><Select id={'review-state-' + finding.id} name="status" defaultValue={finding.status}>
        {['Open', 'UnderReview', 'AwaitingExternalCorrection', 'Acknowledged'].map(x => <option key={x} value={x}>{status(x)}</option>)}</Select></Field>
      <Field id={'review-note-' + finding.id} label="Investigation note"><Input id={'review-note-' + finding.id} name="note" maxLength={1200} required /></Field>
      {reviewers.length > 0 && <Field id={'review-assign-' + finding.id} label="Authorized reviewer"><Select id={'review-assign-' + finding.id} name="reviewer" defaultValue={finding.reviewer ?? ''}>
        <option value="">Keep current assignment</option>{reviewers.map(r => <option key={r.id} value={r.id}>{r.fullName}</option>)}</Select></Field>}
    </div><div><Button type="submit" disabled={action.busy}>Save investigation event</Button></div></form>}
    {action.error && <ErrorState>{action.error}</ErrorState>}{action.notice && <p role="status">{action.notice}</p>}
  </details>
}

function ReferencePanel({ base }: { base: string }) {
  const coverage = useQuery({ queryKey: ['reconciliation', base, 'coverage'], queryFn: () => api<CoverageList>(base + '/coverage'), retry: false })
  const [connection, setConnection] = useState('')
  const [type, setType] = useState<ReconcileType>('SalesInvoice')
  const [search, setSearch] = useState('')
  const [row, setRow] = useState<ReferenceRow>()
  const [source, setSource] = useState('')
  const action = useConnectAction()
  const ref = useQuery({ queryKey: ['reconciliation', base, 'references', connection, type, search], enabled: !!connection, retry: false,
    queryFn: () => api<References>(base + '/references?' + new URLSearchParams({ connection, type, search })) })
  function retrieve(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (!row) return
    const purpose = new FormData(e.currentTarget).get('purpose')
    void action.run(async () => {
      const result = await api<{ invoiceSourceKey?: string }>(base + '/references', 'POST', { connectionId: connection, type, externalId: row.id, purpose })
      setSource(result.invoiceSourceKey ?? '')
      await ref.refetch()
    }, 'Historical evidence recorded')
  }
  return <Card className="workspace-card connect-stack"><h2>Historical accounting references</h2>
    <div className="connect-grid">
      <Field id="history-reference-connection" label="Historical accounting connection"><Select id="history-reference-connection" value={connection}
        onChange={e => { setConnection(e.target.value); setRow(undefined); setSource('') }}><option value="">Choose an accounting connection</option>
        {coverage.data?.links.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field>
      <Field id="history-reference-type" label="Historical document type"><Select id="history-reference-type" value={type}
        onChange={e => { setType(e.target.value as ReconcileType); setRow(undefined); setSource('') }}>
        {Object.entries(typeNames).map(([id, label]) => <option key={id} value={id}>{label}</option>)}</Select></Field>
      <Field id="history-reference-search" label="Search external document number"><Input id="history-reference-search" type="search" maxLength={80}
        value={search} onChange={e => { setSearch(e.target.value); setRow(undefined); setSource('') }} /></Field>
    </div>
    {ref.isError && <ErrorState>{errorMessage(ref.error)}</ErrorState>}
    {connection && ref.isPending && <LoadingState label="Retrieving authorized references…" />}
    {ref.data && <><ScrollTable name="Historical references"><thead><tr><th>External number</th><th>Date</th><th>Source status</th></tr></thead>
      <tbody>{ref.data.items.map(x => <tr key={x.id}><td><Button type="button" variant="light" onClick={() => { setRow(x); setSource('') }}>{x.number}</Button></td>
        <td>{x.date}</td><td>{status(x.status)}</td></tr>)}</tbody></ScrollTable>
      {type === 'SalesInvoice' && <p>{ref.data.creditMemoEligibility}</p>}
      {!!ref.data.saved.length && <details><summary>Recorded reference evidence</summary><ul>{ref.data.saved.map(x =>
        <li key={x.id}>{x.number} · {x.purpose} · {manilaTimestamp.format(new Date(x.at))}</li>)}</ul></details>}</>}
    {row && <form className="connect-stack" onSubmit={retrieve}><h3>{row.number}</h3>
      <Field id="historical-purpose" label="Retrieval purpose"><Input id="historical-purpose" name="purpose" maxLength={300} required /></Field>
      <div><Button type="submit" disabled={action.busy}>Record historical evidence</Button></div></form>}
    {source && <Link className="text-link" to={invoiceFromSource(source)}>Open invoice form from historical Sales Order</Link>}
    {action.error && <ErrorState>{action.error}</ErrorState>}{action.notice && <p role="status">{action.notice}</p>}
  </Card>
}

function RunDocument({ base, id }: { base: string; id: string }) {
  const action = useConnectAction()
  const cache = useQueryClient()
  return <div><Button type="button" variant="light" disabled={action.busy} onClick={() => void action.run(async () => {
    await api(base + '/jobs', 'POST', { documentId: id, key: crypto.randomUUID() })
    await cache.invalidateQueries({ queryKey: ['reconciliation', base] })
  }, 'Reconciliation job queued')}>Reconcile now</Button>
    {action.notice && <p role="status">{action.notice}</p>}{action.error && <ErrorState>{action.error}</ErrorState>}</div>
}

export function DocumentReconciliationPanel({ base, id }: { base: string; id: string }) {
  const query = useQuery({ queryKey: ['reconciliation', base, 'document', id], queryFn: () => api<DocumentReconciliation>(base + '/documents/' + id),
    retry: false, refetchInterval: readInterval })
  if (!query.data) return null
  const latest = query.data.items[0]
  return <Card className="workspace-card connect-stack"><h3>Financial reconciliation</h3>
    <dl className="workspace-definitions"><div><dt>Latest outcome</dt><dd>{status(latest?.status ?? 'NotYetReconciled')}</dd></div>
      {latest && <><div><dt>Coverage and verification</dt><dd>{status(latest.coverage)} · {status(latest.completeness)}{latest.stale && ' · Stale'}</dd></div>
        <div><dt>Latest verification</dt><dd>{manilaTimestamp.format(new Date(latest.at))}</dd></div>
        <div><dt>Financial difference</dt><dd>{amount(latest.difference, latest.currency)}</dd></div></>}
    </dl>
    {query.data.canRun && <RunDocument base={base} id={id} />}
    {latest && <><Link className="text-link" to={reconciliationRoot + '/comparison/' + latest.id}>View comparison</Link>
      <details><summary>Reconciliation history</summary><CheckTable rows={query.data.items} /></details></>}
  </Card>
}

