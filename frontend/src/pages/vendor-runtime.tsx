import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { Badge, Button, Card, ErrorState, Field, Input, LoadingState, Select } from '@/components/ui'
import { api, errorMessage } from '@/lib/account-api'
import type { ConnectorConfiguration, SpecReview } from '@/lib/connect-api'
import { documentKinds, documentKindLabels, directions, directionLabels, pretty, type Direction, type DocumentKind } from '@/lib/mapping-api'
import { ScrollTable } from './connect'
import { useConnectAction } from './field-mapping'

const path = '/vendor/connectors/runtime'
type Policy = { id: string; connectorVersionId: string; capability: string; method: string; path: string; authentication: string; timeoutMs: number; pageLimit: number }
type Runtime = { versions: { id: string; number: number; configuration: ConnectorConfiguration; specification?: SpecReview }[];
  policies: Policy[]; scenarios: string[]; sandbox: string }
type Result = { id: string; request: unknown; result: { status: string; httpStatus: number | null; durationMs: number; remaining: number | null; retryAfter: number | null; response: unknown } }
type Monitor = { releases: { id: string; connectorId: string; name: string; number: number }[];
  availability: { state: string; count: number }[]; connections: { status: string; connectorVersionId: string; count: number }[];
  jobs: { state: string; count: number }[]; failures: { category: string; count: number }[];
  mappingFailures: number; rateLimitEvents: number; latestAttempt?: string;
  reconciliationJobs: { state: string; count: number }[]; reconciliationFailures: { category: string; count: number }[];
  reconciliationFindings: { category: string; count: number }[]; coverageVersions: number; staleVerifications: number; latestReconciliationCompletion?: string }
const command = (code: string) => code.replace(/^SalesInvoice/, 'Sales Invoice ').replace(/^CreditMemo/, 'Credit Memo ').replace(/^SalesOrder/, 'Sales Order ')

export function VendorRuntime({ section }: { section: string }) {
  const info = useQuery({ queryKey: ['vendor-connector-runtime'], queryFn: () => api<Runtime>(path + '/options'), retry: false })
  if (section === 'monitor') return <VendorMonitoring />
  if (info.isPending) return <LoadingState label="Loading sandbox policies…" />
  if (info.isError) return <ErrorState>{errorMessage(info.error)}</ErrorState>
  return section === 'mapping-templates' ? <VendorTemplates options={info.data} /> :
    <div className="connect-stack"><SandboxTest options={info.data} /><ApproveEndpoint options={info.data} refresh={() => info.refetch()} /></div>
}

function SandboxTest({ options }: { options: Runtime }) {
  const [version, setVersion] = useState('')
  const [selected, setSelected] = useState('')
  const [scenario, setScenario] = useState('Normal')
  const [page, setPage] = useState('')
  const [parameters, setParameters] = useState('')
  const [input, setInput] = useState('')
  const [query, setQuery] = useState('')
  const [headers, setHeaders] = useState('')
  const [response, setResponse] = useState<Result>()
  const action = useConnectAction()
  const policy = options.policies.find(x => x.id === selected)
  function submit(event: FormEvent) {
    event.preventDefault()
    void action.run(async () => {
      setResponse(undefined)
      setResponse(await api<Result>(path + '/test', 'POST', { policyId: selected, scenario, sandbox: 'accounting-local',
        page: page ? Number(page) : null, pathParameters: parameters ? JSON.parse(parameters) : null, body: input ? JSON.parse(input) : null,
        query: query ? JSON.parse(query) : null, headers: headers ? JSON.parse(headers) : null }))
    })
  }
  return <Card className="workspace-card connect-stack"><h2>API sandbox</h2>
    <p>Execute a reviewed operation against the isolated development accounting transport. Preview and test results mask authentication headers.</p>
    <form className="connect-stack" onSubmit={submit}><div className="connect-grid">
      <Field id="sandbox-version" label="Sandbox connector version"><Select id="sandbox-version" value={version} required onChange={e => { setVersion(e.target.value); setSelected(''); setResponse(undefined) }}>
        <option value="">Select a connector version</option>{options.versions.map(v => <option key={v.id} value={v.id}>{v.configuration.name} · v{v.number}</option>)}
      </Select></Field>
      <Field id="sandbox-policy" label="Approved endpoint"><Select id="sandbox-policy" value={selected} required onChange={e => { setSelected(e.target.value); setResponse(undefined) }}>
        <option value="">Select an approved operation</option>{options.policies.filter(p => p.connectorVersionId === version).map(p =>
          <option key={p.id} value={p.id}>{p.method} {p.path} · {command(p.capability)}</option>)}</Select></Field>
      <Field id="sandbox-target" label="Authorized sandbox"><Select id="sandbox-target" defaultValue="accounting-local"><option value="accounting-local">{options.sandbox}</option></Select></Field>
      <Field id="sandbox-result" label="Synthetic response"><Select id="sandbox-result" value={scenario} onChange={e => setScenario(e.target.value)}>
        {options.scenarios.map(s => <option key={s} value={s}>{pretty(s)}</option>)}</Select></Field>
      {policy && <Field id="sandbox-page" label="Page (optional)"><Input id="sandbox-page" type="number" min="1" max={policy.pageLimit} value={page} onChange={e => setPage(e.target.value)} /></Field>}
      {policy?.path.includes('{') && <Field id="sandbox-parameters" label="Synthetic path parameters (JSON)"><Input id="sandbox-parameters" value={parameters} onChange={e => setParameters(e.target.value)} /></Field>}
    </div>
    {policy && <details className="connect-order"><summary>Approved query parameters and headers</summary><div className="connect-grid">
      <Field id="sandbox-query" label="Synthetic query parameters (JSON)"><Input id="sandbox-query" value={query} onChange={e => setQuery(e.target.value)} /></Field>
      <Field id="sandbox-headers" label="Synthetic request headers (JSON)"><Input id="sandbox-headers" value={headers} onChange={e => setHeaders(e.target.value)} /></Field>
    </div></details>}
    {policy?.method === 'POST' && <Field id="sandbox-input" label="Synthetic request body (JSON)"><textarea id="sandbox-input" className="control" value={input} rows={4} onChange={e => setInput(e.target.value)} /></Field>}
    {policy && <details className="connect-order"><summary>Preview sanitized request</summary><p>{policy.method} {policy.path}</p>
      <p>{policy.authentication} · credential values redacted · {policy.timeoutMs} ms · {policy.pageLimit} pages</p></details>}
    {action.error && <ErrorState>{action.error}</ErrorState>}
    <div><Button type="submit" disabled={!selected || action.busy}>Execute sandbox request</Button></div></form>
    {response && <section><h3>Sandbox result</h3><Badge tone={response.result.status === 'Confirmed' ? 'success' : 'warning'}>{pretty(response.result.status)}</Badge>
      <p>HTTP {response.result.httpStatus ?? '—'} · {response.result.durationMs} ms · {response.result.remaining ?? '—'} requests remaining</p>
      {response.result.retryAfter !== null && <p>Retry after {response.result.retryAfter} seconds.</p>}
      <details className="connect-order"><summary>Inspect sanitized response and transformed fields</summary>
        <pre className="connect-schema">{JSON.stringify(response.result.response, null, 2)}</pre></details></section>}
  </Card>
}

function ApproveEndpoint({ options, refresh }: { options: Runtime; refresh: () => Promise<unknown> }) {
  const [version, setVersion] = useState('')
  const [endpoint, setEndpoint] = useState('')
  const [capability, setCapability] = useState('')
  const [authentication, setAuthentication] = useState('Synthetic Authorization')
  const action = useConnectAction()
  const active = options.versions.find(x => x.id === version)
  const operations = active?.specification?.endpoints.filter(x => x.method === 'GET' || x.method === 'POST') ?? []
  function submit(event: FormEvent) {
    event.preventDefault()
    const selected = operations.find(x => x.method + ' ' + x.path === endpoint)
    if (!selected) return
    void action.run(async () => {
      await api(path + '/policies', 'POST', { connectorVersionId: version, capability, method: selected.method, path: selected.path,
        authentication, timeoutMs: 3000, pageLimit: 3 })
      await refresh()
    }, 'Sandbox policy approved')
  }
  return <Card className="workspace-card"><h3>Approve a reviewed sandbox operation</h3><p>Approval requires a published sandbox connector and a reviewed specification for the approved development origin.</p>
    <form className="connect-stack" onSubmit={submit}><div className="connect-grid">
      <Field id="policy-version" label="Reviewed connector version"><Select id="policy-version" value={version} onChange={e => { setVersion(e.target.value); setEndpoint(''); setCapability('') }}>
        <option value="">Select a reviewed version</option>{options.versions.filter(x => !!x.specification).map(x => <option key={x.id} value={x.id}>{x.configuration.name} · v{x.number}</option>)}</Select></Field>
      <Field id="policy-operation" label="Reviewed operation"><Select id="policy-operation" value={endpoint} onChange={e => setEndpoint(e.target.value)}>
        <option value="">Select an operation</option>{operations.map(x => <option key={x.method + x.path}>{x.method} {x.path}</option>)}</Select></Field>
      <Field id="policy-capability" label="Verified capability"><Select id="policy-capability" value={capability} onChange={e => setCapability(e.target.value)}>
        <option value="">Select a capability</option>{active?.configuration.capabilities.filter(c => c.verification === 'Development Verified').map(c => <option key={c.code} value={c.code}>{pretty(c.code)}</option>)}</Select></Field>
      <Field id="policy-authentication" label="Sandbox authentication"><Select id="policy-authentication" value={authentication} onChange={e => setAuthentication(e.target.value)}>
        {active?.configuration.authentication.map(x => <option key={x}>{x}</option>) ?? <option>Synthetic Authorization</option>}</Select></Field>
    </div>{action.error && <ErrorState>{action.error}</ErrorState>}{action.notice && <p role="status">{action.notice}</p>}
      <div><Button type="submit" disabled={!version || !endpoint || !capability || action.busy}>Approve sandbox operation</Button></div></form></Card>
}

function VendorTemplates({ options }: { options: Runtime }) {
  const [name, setName] = useState('')
  const [version, setVersion] = useState('')
  const [type, setType] = useState<DocumentKind>('SalesInvoice')
  const [direction, setDirection] = useState<Direction>('Outbound')
  const [rules, setRules] = useState('')
  const action = useConnectAction()
  const templatesPath = '/vendor/connectors/mapping-templates'
  const templates = useQuery({ queryKey: ['vendor-mapping-templates'], queryFn: () => api<{ items: {
    id: string; name: string; connectorVersionId: string; documentType: DocumentKind; direction: Direction; version: number
  }[] }>(templatesPath), retry: false })
  function publish(event: FormEvent) {
    event.preventDefault()
    void action.run(async () => {
      await api(templatesPath, 'POST', { name, connectorVersionId: version, documentType: type, direction, rules: rules ? JSON.parse(rules) : null })
      await templates.refetch()
    }, 'Vendor template published')
  }
  return <Card className="workspace-card connect-stack"><h2>Vendor default mapping templates</h2>
    <p>Publish validated rules for one connector version. Companies copy a template into their own version history.</p>
    <form className="connect-stack" onSubmit={publish}><div className="connect-grid">
      <Field id="default-name" label="Template name"><Input id="default-name" value={name} required maxLength={90} onChange={e => setName(e.target.value)} /></Field>
      <Field id="default-version" label="Template connector version"><Select id="default-version" value={version} required onChange={e => setVersion(e.target.value)}>
        <option value="">Select connector version</option>{options.versions.map(x => <option key={x.id} value={x.id}>{x.configuration.name} · v{x.number}</option>)}</Select></Field>
      <Field id="default-type" label="Template document type"><Select id="default-type" value={type} onChange={e => setType(e.target.value as DocumentKind)}>
        {documentKinds.map(x => <option key={x} value={x}>{documentKindLabels[x]}</option>)}</Select></Field>
      <Field id="default-direction" label="Template direction"><Select id="default-direction" value={direction} onChange={e => setDirection(e.target.value as Direction)}>
        {directions.map(x => <option key={x} value={x}>{directionLabels[x]}</option>)}</Select></Field>
    </div><Field id="default-rules" label="Declarative rules (optional JSON)"><textarea id="default-rules" className="control" rows={4} value={rules}
      placeholder="Leave empty to publish the reviewed built-in field set." onChange={e => setRules(e.target.value)} /></Field>
    {action.error && <ErrorState>{action.error}</ErrorState>}{action.notice && <p role="status">{action.notice}</p>}
    <div><Button type="submit" disabled={!version || action.busy}>Publish mapping template</Button></div></form>
    {templates.isError ? <ErrorState>{errorMessage(templates.error)}</ErrorState> : templates.data && <ScrollTable name="Published mapping templates">
      <thead><tr><th>Template</th><th>Document</th><th>Direction</th><th>Version</th></tr></thead>
      <tbody>{templates.data.items.map(x => <tr key={x.id}><td>{x.name}</td><td>{documentKindLabels[x.documentType]}</td>
        <td>{directionLabels[x.direction]}</td><td>{x.version}</td></tr>)}</tbody></ScrollTable>}
  </Card>
}

function VendorMonitoring() {
  const stats = useQuery({ queryKey: ['vendor-connector-monitor'], queryFn: () => api<Monitor>(path + '/monitor'), retry: false })
  if (stats.isPending) return <LoadingState label="Loading operational metrics…" />
  if (stats.isError) return <ErrorState>{errorMessage(stats.error)}</ErrorState>
  const value = stats.data
  const releases = new Map(value.releases.map(item => [item.id, item]))
  const sections = [
    ['Connector availability', value.availability.map(x => [x.state, x.count] as const)],
    ['Synchronization queue', value.jobs.map(x => [pretty(x.state), x.count] as const)],
    ['Failure categories', value.failures.map(x => [pretty(x.category), x.count] as const)],
    ['Reconciliation queue', value.reconciliationJobs.map(x => [pretty(x.state), x.count] as const)],
    ['Reconciliation failures', value.reconciliationFailures.map(x => [pretty(x.category), x.count] as const)],
    ['Reconciliation findings', value.reconciliationFindings.map(x => [pretty(x.category), x.count] as const)],
  ] as const
  return <div className="connect-stack"><Card className="workspace-card"><h2>Connector operations</h2>
    <dl className="workspace-definitions"><div><dt>Mapping validation failures</dt><dd>{value.mappingFailures}</dd></div>
      <div><dt>Sandbox rate-limit events</dt><dd>{value.rateLimitEvents}</dd></div>
      <div><dt>Last completed accounting attempt</dt><dd>{value.latestAttempt ? new Date(value.latestAttempt).toLocaleString() : '—'}</dd></div>
      <div><dt>Last completed reconciliation job</dt><dd>{value.latestReconciliationCompletion ? new Date(value.latestReconciliationCompletion).toLocaleString() : '—'}</dd></div>
      <div><dt>Coverage versions</dt><dd>{value.coverageVersions}</dd></div><div><dt>Stale verifications</dt><dd>{value.staleVerifications}</dd></div></dl></Card>
    <Card className="workspace-card"><h3>Connection health by version</h3>
      {value.connections.length ? <ScrollTable name="Connection health by version"><thead><tr><th>Connector release</th><th>Status</th><th>Connections</th></tr></thead>
        <tbody>{value.connections.map(row => {
          const release = releases.get(row.connectorVersionId)
          return <tr key={row.connectorVersionId + ':' + row.status}><td>{release
            ? <Link className="text-link" to={'/portal/vendor/connectors/' + release.connectorId + '?version=' + release.id}>{release.name} · v{release.number}</Link>
            : row.connectorVersionId}</td><td>{row.status}</td><td>{row.count}</td></tr>
        })}</tbody></ScrollTable> : <p>No connections have been recorded.</p>}
    </Card>
    <div className="connect-grid">{sections.map(([title, entries]) => <Card className="workspace-card" key={title}><h3>{title}</h3>
      <ul>{entries.map(([label, count]) => <li key={label}>{label} · {count}</li>)}</ul></Card>)}</div></div>
}

