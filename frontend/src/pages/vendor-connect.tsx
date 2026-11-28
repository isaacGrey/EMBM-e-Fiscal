import { useState, type ComponentProps, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, FileUp, Plug, Plus } from 'lucide-react'
import { Link, useParams, useNavigate, useSearchParams } from 'react-router-dom'
import { Badge, Button, Card, ErrorState, Field, Input, LoadingState, Select } from '@/components/ui'
import { api, errorMessage, manilaTimestamp } from '@/lib/account-api'
import type { ConnectorConfiguration, ConnectorVersion, SpecReview } from '@/lib/connect-api'
import { ScrollTable } from './connect'
import { VendorRuntime } from './vendor-runtime'
import { VendorProviderReadiness } from './vendor-provider-readiness'
import './connect.css'

const endpoint = '/vendor/connectors'
const route = '/portal/vendor/connectors'
type Options = { categories: string[]; deployments: string[]; authentication: string[]; accounting: string[]; commerce: string[] }
type Catalog = { items: ConnectorVersion[]; canEdit: boolean; canApprove: boolean; canPublish: boolean }
type Detail = { versions: ConnectorVersion[]; audit: { action: string; at: string; actorId?: string }[] }
type Impact = { connections: { status: string; count: number }[]; audit: Detail['audit']
  sync: { unsubmitted: number; awaitingConfirmation: number; confirmed: number } }
function Textarea(props: ComponentProps<'textarea'>) {
  return <textarea className="control connect-textarea" {...props} />
}

const empty: ConnectorConfiguration = {
  name: '', description: '', deployment: 'Configurable REST API', apiVersion: '', editions: [], regions: [], documentation: [], requirements: [],
  authentication: [], priority: 'Additional', logoLabel: '', capabilities: [], origins: [], manualOperations: [],
}
const display = (value: string) => value.replace(/([a-z])([A-Z])/g, '$1 $2')
const lines = (value: string) => value.split('\n').map(x => x.trim()).filter(Boolean)

export function VendorConnect() {
  const { connectorId } = useParams()
  const query = useQuery({ queryKey: ['vendor-connect'], queryFn: () => api<Catalog>(endpoint), retry: false })
  const choices = useQuery({ queryKey: ['vendor-connect-choices'], queryFn: () => api<Options>(endpoint + '/choices'), retry: false })
  const [term, setTerm] = useState('')
  if (query.isPending || choices.isPending) return <LoadingState label="Loading Connector Builder…" />
  if (query.isError || choices.isError) return <ErrorState>{errorMessage(query.error ?? choices.error)}</ErrorState>
  if (connectorId && ['api-sandbox', 'mapping-templates', 'monitor'].includes(connectorId)) return <VendorRuntime section={connectorId} />
  if (connectorId === 'production-readiness') return <VendorProviderReadiness />
  const options = choices.data
  if (connectorId === 'new') return query.data.canEdit
    ? <VendorEditor key="new" choices={options} canEdit canApprove={query.data.canApprove} canPublish={query.data.canPublish} />
    : <ErrorState>Connector editing permission is required.</ErrorState>
  if (connectorId) return <VendorDetail key={connectorId} id={connectorId} choices={options}
    canEdit={query.data.canEdit} canApprove={query.data.canApprove} canPublish={query.data.canPublish} />
  const rows = query.data.items.filter(row => [row.configuration.name, row.code, row.category].join(' ').toLowerCase().includes(term.toLowerCase().trim()))
  return <div className="connect-stack">
    <Card className="workspace-card connect-intro"><span className="connect-mark"><Plug size={22} /></span><div>
      <h2>Universal API Connector Builder</h2><p>Configure and review connector versions. Approved synthetic connectors can be published to the development marketplace.</p>
    </div></Card>
    <Card className="workspace-card"><div className="connect-actions">
      <Field id="vendor-connect-search" label="Search connector definitions"><Input id="vendor-connect-search" type="search" value={term} onChange={e => setTerm(e.target.value)} /></Field>
      {query.data.canEdit && <Button asChild><Link to={route + '/new'}><Plus size={16} /> Create connector</Link></Button>}</div></Card>
    <Card className="workspace-card"><ScrollTable name="Connector definitions"><thead><tr><th>Platform</th><th>Category</th><th>Latest version</th><th>State</th><th>Availability</th></tr></thead>
      <tbody>{rows.map(item => <tr key={item.connectorId}><td><Link className="text-link" to={route + '/' + item.connectorId}>{item.configuration.name}</Link></td>
        <td>{item.category}</td><td>{item.number}</td><td>{item.revocation ? 'Revoked' : item.state}</td><td>{item.effectiveAvailability}</td></tr>)}</tbody>
    </ScrollTable>{!rows.length && <p>No definitions match this search.</p>}</Card>
  </div>
}

function VendorDetail({ id, choices, canEdit, canApprove, canPublish }: { id: string; choices: Options; canEdit: boolean; canApprove: boolean; canPublish: boolean }) {
  const query = useQuery({ queryKey: ['vendor-connect-detail', id], queryFn: () => api<Detail>(endpoint + '/' + id), retry: false })
  const [params, setParams] = useSearchParams()
  const selected = params.get('version')
  const setSelected = (value: string) => setParams({ version: value }, { replace: true })
  if (query.isPending) return <LoadingState label="Loading connector versions…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  const version = query.data.versions.find(x => x.id === selected) ?? query.data.versions[0]
  return <div className="connect-stack">
    <Card className="workspace-card"><h2>{version.configuration.name}</h2>
      <Field id="connector-version" label="Version"><Select id="connector-version" value={version.id} onChange={e => setSelected(e.target.value)}>
        {query.data.versions.map(x => <option value={x.id} key={x.id}>Version {x.number} · {x.revocation ? 'Revoked' : x.state}</option>)}
      </Select></Field></Card>
    <VendorEditor key={version.id + ':' + version.revision} choices={choices} version={version} canEdit={canEdit} canApprove={canApprove} canPublish={canPublish}
      select={setSelected} />
    <ReleaseImpact key={version.id} version={version} />
    <Card className="workspace-card"><h3>Connector audit history</h3>{query.data.audit.length ? <ScrollTable name="Connector audit history">
      <thead><tr><th>Event</th><th>Time</th></tr></thead>
      <tbody>{query.data.audit.map((x, index) => <tr key={x.at + index}><td>{display(x.action.replace('Connectors.', ''))}</td>
        <td>{manilaTimestamp.format(new Date(x.at))}</td></tr>)}</tbody>
    </ScrollTable> : <p>No recorded connector changes.</p>}</Card>
  </div>
}

function ReleaseImpact({ version }: { version: ConnectorVersion }) {
  const impact = useQuery({ queryKey: ['vendor-release-impact', version.connectorId, version.id],
    queryFn: () => api<Impact>(endpoint + '/' + version.connectorId + '/versions/' + version.id + '/impact'), retry: false })
  return <Card className="workspace-card connect-stack"><h3>Version {version.number} impact</h3>
    {version.revocation && <p><Badge tone="warning">Revoked</Badge> {version.revocation.reason} · {manilaTimestamp.format(new Date(version.revocation.at))}</p>}
    {impact.isPending ? <LoadingState /> : impact.isError ? <ErrorState>{errorMessage(impact.error)}</ErrorState> : <>
      {impact.data.connections.length ? <dl className="workspace-definitions">{impact.data.connections.map(x => <div key={x.status}><dt>{x.status} connections</dt><dd>{x.count}</dd></div>)}</dl>
        : <p>No connections are pinned to this version.</p>}
      <dl className="workspace-definitions"><div><dt>Unsubmitted accounting operations</dt><dd>{impact.data.sync.unsubmitted}</dd></div>
        <div><dt>Awaiting accounting confirmation</dt><dd>{impact.data.sync.awaitingConfirmation}</dd></div>
        <div><dt>Confirmed accounting operations</dt><dd>{impact.data.sync.confirmed}</dd></div></dl>
      {impact.data.audit.length > 0 && <ScrollTable name="Version audit history">
        <thead><tr><th>Version event</th><th>Time</th></tr></thead>
        <tbody>{impact.data.audit.map((x, index) => <tr key={x.at + index}><td>{display(x.action.replace('Connectors.', ''))}</td>
          <td>{manilaTimestamp.format(new Date(x.at))}</td></tr>)}</tbody>
      </ScrollTable>}
    </>}
  </Card>
}

function VendorEditor({ choices, version, canEdit, canApprove, canPublish, select }: { choices: Options; version?: ConnectorVersion; canEdit: boolean;
  canApprove: boolean; canPublish: boolean; select?: (id: string) => void }) {
  const [config, setConfig] = useState(version?.configuration ?? empty)
  const [code, setCode] = useState(version?.code ?? '')
  const [category, setCategory] = useState(version?.category ?? choices.categories[0])
  const [adapter, setAdapter] = useState(version?.adapter ?? 'review-only')
  const [flows, setFlows] = useState(config.manualOperations.map(x => x.method + ' ' + x.path).join('\n'))
  const [fields, setFields] = useState({
    editions: config.editions.join('\n'), regions: config.regions.join('\n'), documentation: config.documentation.join('\n'),
    requirements: config.requirements.join('\n'), origins: config.origins.join('\n'),
  })
  const [file, setFile] = useState<File>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const navigate = useNavigate()
  const cache = useQueryClient()
  const editable = canEdit && (!version || ['Draft Configuration', 'Awaiting Validation', 'Validated'].includes(version.state))
  const path = version ? endpoint + '/' + version.connectorId + '/versions/' + version.id : endpoint
  const allowed = category === 'Accounting Software' ? choices.accounting : category === 'E-Commerce' ? choices.commerce
    : [...new Set([...choices.accounting, ...choices.commerce])]
  function update<K extends keyof ConnectorConfiguration>(key: K, value: ConnectorConfiguration[K]) { setConfig(prev => ({ ...prev, [key]: value })) }
  function changeCategory(value: string) {
    setCategory(value); setAdapter('review-only'); update('capabilities', [])
    update('authentication', config.authentication.filter(x => x !== 'Synthetic Authorization'))
  }
  async function refresh() {
    await Promise.all([cache.invalidateQueries({ queryKey: ['vendor-connect'] }),
      cache.invalidateQueries({ queryKey: ['vendor-connect-detail', version?.connectorId] }),
      cache.invalidateQueries({ queryKey: ['vendor-release-impact', version?.connectorId] }),
      cache.invalidateQueries({ queryKey: ['vendor-connector-monitor'] }),
      cache.invalidateQueries({ queryKey: ['vendor-connector-runtime'] })])
  }
  async function run(action: () => Promise<void>) {
    setBusy(true); setError(''); setNotice('')
    try { await action() }
    catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  function save(event: FormEvent) {
    event.preventDefault()
    void run(async () => {
      const manualOperations = lines(flows).map(line => {
        const [method, ...pathParts] = line.split(/\s+/)
        return { method, path: pathParts.join(' ') }
      })
      const configuration = { ...config, editions: lines(fields.editions), regions: lines(fields.regions), documentation: lines(fields.documentation),
        requirements: lines(fields.requirements), origins: lines(fields.origins), manualOperations }
      const saved = await api<ConnectorVersion>(path, version ? 'PUT' : 'POST', { code, category, adapter, configuration, expectedRevision: version?.revision ?? 0 })
      if (version) select?.(saved.id)
      await refresh()
      if (!version) navigate(route + '/' + saved.connectorId, { replace: true })
    })
  }

  function step(command: 'validate' | 'approve' | 'publish' | 'copy' | 'disable' | 'resume' | 'deprecate' | 'retire') {
    if (!version) return
    void run(async () => {
      if (command === 'disable' || command === 'resume') await api(endpoint + '/' + version.connectorId + '/' + command, 'POST')
      else if (command === 'deprecate' || command === 'retire')
        await api(endpoint + '/' + version.connectorId + '/lifecycle', 'POST', { state: command === 'deprecate' ? 'Deprecated' : 'Retired' })
      else {
        const result = await api<ConnectorVersion>(path + '/' + command, 'POST', command === 'copy' ? undefined : { expectedRevision: version.revision })
        select?.(result.id)
      }
      await refresh()
    })
  }
  function revoke(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const reason = new FormData(event.currentTarget).get('reason')
    if (!window.confirm('Revoke this version and suspend connections pinned to it?')) return
    void run(async () => {
      await api(path + '/revoke', 'POST', { reason })
      await refresh()
      setNotice('Version revoked.')
    })
  }
  function upload(event: FormEvent) {
    event.preventDefault()
    void run(async () => {
      if (!file || !version) return
      const data = new FormData()
      data.append('file', file)
      await api(path + '/import?expectedRevision=' + version.revision, 'POST', data)
      await refresh()
      setNotice('Specification normalized for review.')
    })
  }

  return <div className="connect-stack">
    <Link className="back-link" to={route}><ArrowLeft size={16} /> Connector definitions</Link>
    <Card className="workspace-card"><div className="connect-actions"><h3>{version ? 'Version ' + version.number : 'Create connector'}</h3>
      {version && <Badge tone={version.revocation ? 'warning' : version.state === 'Published' ? 'success' : 'neutral'}>
        {version.revocation ? 'Revoked' : version.state}</Badge>}
      {version?.disabled && <Badge tone="warning">Disabled</Badge>}
      {version?.lifecycle && <Badge tone="warning">{version.lifecycle}</Badge>}</div>
      <form onSubmit={save}><fieldset disabled={busy || !editable} className="connect-stack connect-fieldset">
        <div className="connect-grid">
          <Field id="builder-code" label="Connector code"><Input id="builder-code" value={code} readOnly={!!version} required maxLength={55}
            onChange={e => setCode(e.target.value)} /></Field>
          <Field id="builder-name" label="Platform name"><Input id="builder-name" value={config.name} required maxLength={100}
            onChange={e => update('name', e.target.value)} /></Field>
          <Field id="builder-category" label="Connector category"><Select id="builder-category" value={category} onChange={e => changeCategory(e.target.value)}>
            {choices.categories.map(x => <option key={x}>{x}</option>)}</Select></Field>
          <Field id="builder-adapter" label="Execution adapter"><Select id="builder-adapter" value={adapter} onChange={e => {
            setAdapter(e.target.value)
            if (e.target.value === 'review-only') {
              update('capabilities', config.capabilities.map(cap => ({ ...cap, verification: 'Proposed' })))
              update('authentication', config.authentication.filter(x => x !== 'Synthetic Authorization'))
            }
          }}>
            <option value="review-only">Review only</option>
            {category === 'Accounting Software' && <option value="mock-accounting">Mock accounting</option>}
            {category === 'E-Commerce' && <option value="mock-commerce">Mock commerce</option>}
          </Select></Field>
          <Field id="builder-deployment" label="Deployment type"><Select id="builder-deployment" value={config.deployment} onChange={e => update('deployment', e.target.value)}>
            {choices.deployments.map(x => <option key={x}>{x}</option>)}</Select></Field>
          <Field id="builder-priority" label="Implementation priority"><Select id="builder-priority" value={config.priority} onChange={e => update('priority', e.target.value)}>
            <option>Additional</option><option>Planned</option><option>Primary</option></Select></Field>
          <Field id="builder-api-version" label="API version"><Input id="builder-api-version" value={config.apiVersion} maxLength={42}
            onChange={e => update('apiVersion', e.target.value)} /></Field>
          <Field id="builder-logo" label="Platform initials"><Input id="builder-logo" value={config.logoLabel ?? ''} maxLength={5}
            onChange={e => update('logoLabel', e.target.value)} /></Field>
        </div>
        <Field id="builder-description" label="Platform description"><Textarea id="builder-description" required value={config.description} maxLength={500}
          onChange={e => update('description', e.target.value)} /></Field>
        <details className="connect-order"><summary>Editions, regions and provider requirements</summary><div className="connect-grid">
          {Object.entries({ editions: 'Product editions', regions: 'Supported regions', documentation: 'Documentation URLs',
            requirements: 'Connection requirements', origins: 'Approved origins for review' }).map(([key, label]) =>
            <Field key={key} id={'builder-' + key} label={label}><Textarea id={'builder-' + key} value={fields[key as keyof typeof fields]} rows={3}
              placeholder="One value per line" onChange={e => setFields(old => ({ ...old, [key]: e.target.value }))} /></Field>)}
        </div></details>
        <details className="connect-order"><summary>Authentication definitions</summary>
          <p>Choose declarative mechanisms. Store provider credential references through approved infrastructure when the provider is enabled.</p>
          <div className="connect-grid">{choices.authentication.filter(x => x !== 'Synthetic Authorization' || adapter !== 'review-only').map(x =>
            <label key={x} className="connect-check"><input type="checkbox" checked={config.authentication.includes(x)} onChange={e =>
              update('authentication', e.target.checked ? [...config.authentication, x] : config.authentication.filter(a => a !== x))} />{x}</label>)}</div>
        </details>
        <details className="connect-order"><summary>Capabilities</summary><div className="connect-capability-editor">
          {allowed.map(x => {
            const capability = config.capabilities.find(c => c.code === x)
            return <div className="connect-capability-row" key={x}>
              <label className="connect-check"><input type="checkbox" checked={!!capability} onChange={e => update('capabilities', e.target.checked
                ? [...config.capabilities, { code: x, verification: 'Proposed' }] : config.capabilities.filter(c => c.code !== x))} />{display(x)}</label>
              {capability && <><Select aria-label={display(x) + ' verification'} value={capability.verification} onChange={e =>
                update('capabilities', config.capabilities.map(c => c.code !== x ? c : { ...c, verification: e.target.value as typeof capability.verification }))}>
                <option>Proposed</option>{adapter !== 'review-only' && <option>Development Verified</option>}</Select>
                <Input aria-label={display(x) + ' documentation URL'} placeholder="Documentation URL (optional)" type="url" value={capability.documentation ?? ''}
                  onChange={e => update('capabilities', config.capabilities.map(c => c.code !== x ? c : { ...c, documentation: e.target.value }))} /></>}
            </div>
          })}
        </div></details>
        <Field id="builder-operations" label="Manual API operations"><Textarea id="builder-operations" value={flows} rows={3}
          placeholder="One operation per line, for example GET /orders" onChange={e => setFlows(e.target.value)} /></Field>
        {editable && <div><Button type="submit">{version ? 'Save connector configuration' : 'Create draft configuration'}</Button></div>}
      </fieldset></form>
      {error && <ErrorState>{error}</ErrorState>}{notice && <p role="status">{notice}</p>}
      {version && <div className="connect-actions">
        {editable && <Button variant="light" disabled={busy} onClick={() => step('validate')}>Validate configuration</Button>}
        {canApprove && version.state === 'Validated' && adapter !== 'review-only' && <Button disabled={busy} onClick={() => step('approve')}>Approve development version</Button>}
        {canPublish && version.state === 'Approved' && version.lifecycle !== 'Retired' && <Button disabled={busy} onClick={() => step('publish')}>Publish development connector</Button>}
        {canEdit && <Button variant="light" disabled={busy} onClick={() => step('copy')}>Create new version</Button>}
        {canPublish && version.state === 'Published' && !version.disabled && <Button variant="light" disabled={busy} onClick={() => step('disable')}>Disable connector</Button>}
        {canPublish && version.state === 'Published' && version.disabled && version.lifecycle !== 'Retired' &&
          <Button variant="light" disabled={busy} onClick={() => step('resume')}>Resume connector</Button>}
        {canPublish && version.state === 'Published' && !version.lifecycle && <Button variant="light" disabled={busy}
          onClick={() => step('deprecate')}>Deprecate connector</Button>}
        {canPublish && version.state === 'Published' && version.lifecycle === 'Deprecated' && <Button variant="light" disabled={busy}
          onClick={() => { if (window.confirm('Retire this connector and suspend its existing connections?')) step('retire') }}>Retire connector</Button>}
      </div>}
      {version && canPublish && version.state === 'Published' && !version.revocation && version.adapter !== 'review-only' &&
        <form className="connect-actions" onSubmit={revoke}>
          <Field id="version-revocation-reason" label="Revocation reason"><Input id="version-revocation-reason" name="reason" required maxLength={500} /></Field>
          <Button type="submit" variant="light" disabled={busy}>Revoke this version</Button>
        </form>}
    </Card>
    {version && editable && <Card className="workspace-card"><h3>Import API specification</h3>
      <p>OpenAPI 3.x, Swagger 2.0, Postman 2.x and eFiscal v1. Maximum 512 KiB.</p>
      <form className="connect-actions" onSubmit={upload}>
        <Field id="connector-specification" label="API specification file"><Input id="connector-specification" type="file" accept=".json,.yaml,.yml"
          onChange={e => setFile(e.target.files?.[0])} /></Field>
        <Button type="submit" disabled={busy || !file}><FileUp size={16} /> Upload specification</Button>
      </form></Card>}
    {version?.specification && <ImportedReview spec={version.specification} />}
    {version?.validation && <Card className="workspace-card"><h3>Validation results</h3><Badge tone={version.validation.valid ? 'success' : 'warning'}>
      {version.validation.valid ? 'Configuration valid' : 'Changes required'}</Badge>
      <ul>{[...version.validation.errors, ...version.validation.warnings].map(x => <li key={x}>{x}</li>)}</ul></Card>}
  </div>
}

function ImportedReview({ spec }: { spec: SpecReview }) {
  return <Card className="workspace-card"><h3>Imported endpoint review</h3>
    <p>{spec.format} · {spec.title || 'Connector configuration'} {spec.version}</p>
    {!!spec.servers.length && <p>Declared origins {spec.servers.join(', ')}</p>}
    {!!spec.authentication.length && <p>Authentication {spec.authentication.join(', ')}</p>}
    <ScrollTable name="Imported API operations"><thead><tr><th>Method</th><th>Path</th><th>Parameters</th><th>Schema review</th></tr></thead>
      <tbody>{spec.endpoints.map(x => <tr key={x.method + x.path}><td><Badge>{x.method}</Badge></td><td>{x.path}</td>
        <td>{x.parameters.map(p => p.name + ' (' + p.location + (p.required ? ', required)' : ')')).join(', ') || '—'}</td>
        <td>{x.request || x.response || x.parameters.some(p => p.schema) ? <details><summary>View schemas</summary>
          <pre className="connect-schema">{JSON.stringify({ request: x.request, response: x.response, parameters: x.parameters }, null, 2)}</pre>
        </details> : '—'}</td></tr>)}</tbody></ScrollTable>
    {!!spec.warnings.length && <><h4>Review notes</h4><ul>{spec.warnings.map(x => <li key={x}>{x}</li>)}</ul></>}
  </Card>
}

