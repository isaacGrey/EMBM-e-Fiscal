import { useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { Badge, Button, Card, ErrorState, Field, Input, LoadingState, Select } from '@/components/ui'
import { api, errorMessage } from '@/lib/account-api'
import { connectRoot, type Connection } from '@/lib/connect-api'
import { directions, directionLabels, documentKinds, documentKindLabels, pretty, type Direction, type DocumentKind,
  type FieldRule, type MappingField, type MappingProfile, type PreviewResult, type ProfileDetail, type ReferenceChoices } from '@/lib/mapping-api'
import { ScrollTable } from './connect'

const root = connectRoot + '/field-mapping'
type List<T> = { items: T[] }

export function useConnectAction() {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  async function run(action: () => Promise<void>, message = '') {
    setBusy(true); setError(''); setNotice('')
    try { await action(); setNotice(message) }
    catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  return { busy, error, notice, run }
}

export function FieldMapping({ base, id }: { base: string; id?: string }) {
  return id ? <Designer key={id} base={base} id={id} /> : <ProfileListing base={base} />
}

function ProfileListing({ base }: { base: string }) {
  const [name, setName] = useState('')
  const [connection, setConnection] = useState('')
  const [type, setType] = useState<DocumentKind>('SalesInvoice')
  const [direction, setDirection] = useState<Direction>('Outbound')
  const actions = useConnectAction()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const profiles = useQuery({ queryKey: ['field-mapping', base], queryFn: () => api<List<MappingProfile>>(base + '/mapping-profiles'), retry: false })
  const connections = useQuery({ queryKey: ['connect', base, 'connections'], queryFn: () => api<List<Connection>>(base + '/connections'), retry: false })
  if (profiles.isPending || connections.isPending) return <LoadingState label="Loading mapping profiles…" />
  if (profiles.isError || connections.isError) return <ErrorState>{errorMessage(profiles.error ?? connections.error)}</ErrorState>
  const links = connections.data.items.filter(c => c.status === 'Connected' && (c.resourceType === 'CompanyFile' || direction === 'Inbound'))
  return <div className="connect-stack">
    <Card className="workspace-card"><h2>Mapping profiles</h2><p>Configure a versioned mapping for a connector, document type and direction.</p>
      <ScrollTable name="Mapping profiles"><thead><tr><th>Profile</th><th>Connection</th><th>Document</th><th>Direction</th><th>Active version</th></tr></thead>
        <tbody>{profiles.data.items.map(p => <tr key={p.id}><td><Link className="text-link" to={root + '/' + p.id}>{p.name}</Link></td><td>{p.connectionName}</td>
          <td>{documentKindLabels[p.documentType]}</td><td>{directionLabels[p.direction]}</td><td>{p.disabled ? 'Disabled' : p.activeVersion ?? '—'}</td></tr>)}</tbody>
      </ScrollTable>{!profiles.data.items.length && <p>No mapping profiles for this company.</p>}</Card>
    <Card className="workspace-card"><h2>New mapping profile</h2>
      <form className="connect-stack" onSubmit={event => {
        event.preventDefault()
        void actions.run(async () => {
          const row = await api<MappingProfile>(base + '/mapping-profiles', 'POST', { name, connectionId: connection, documentType: type, direction })
          await queryClient.invalidateQueries({ queryKey: ['field-mapping', base] })
          navigate(root + '/' + row.id)
        })
      }}><div className="connect-grid">
        <Field id="mapping-name" label="Mapping profile name"><Input id="mapping-name" value={name} required maxLength={80} onChange={e => setName(e.target.value)} /></Field>
        <Field id="mapping-direction" label="Mapping direction"><Select id="mapping-direction" value={direction} onChange={e => { setDirection(e.target.value as Direction); setConnection('') }}>
          {directions.map(x => <option key={x} value={x}>{directionLabels[x]}</option>)}</Select></Field>
        <Field id="mapping-connection" label="Mapping connection"><Select id="mapping-connection" value={connection} required onChange={e => setConnection(e.target.value)}>
          <option value="">Select a company connection</option>{links.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field>
        <Field id="mapping-type" label="Mapping document type"><Select id="mapping-type" value={type} onChange={e => setType(e.target.value as DocumentKind)}>
          {documentKinds.map(x => <option key={x} value={x}>{documentKindLabels[x]}</option>)}</Select></Field>
      </div>{actions.error && <ErrorState>{actions.error}</ErrorState>}
        <div><Button type="submit" disabled={actions.busy || !connection}>Create mapping profile</Button></div>
      </form>
    </Card>
  </div>
}

function Designer({ base, id }: { base: string; id: string }) {
  const path = base + '/mapping-profiles/' + id
  const info = useQuery({ queryKey: ['field-mapping', base, id], queryFn: () => api<ProfileDetail>(path), retry: false })
  if (info.isPending) return <LoadingState label="Loading field definitions…" />
  if (info.isError) return <ErrorState>{errorMessage(info.error)}</ErrorState>
  return <DesignerForm key={info.data.versions[0]?.number ?? 0} detail={info.data} base={base} path={path} />
}

function DesignerForm({ detail, base, path }: { detail: ProfileDetail; base: string; path: string }) {
  const cache = useQueryClient()
  const actions = useConnectAction()
  const { profile, versions, fields } = detail
  const latest = versions[0]
  const [rules, setRules] = useState<FieldRule[]>(latest?.rules ?? detail.defaults)
  const [scope, setScope] = useState('Header')
  const [search, setSearch] = useState('')
  const [template, setTemplate] = useState('')
  const [preview, setPreview] = useState<PreviewResult>()
  const [extra, setExtra] = useState<Record<string, string>>({})
  const [document, setDocument] = useState('')
  const selected = fields.destination.filter(f => f.scope === scope && (f.label + f.code + f.type).toLowerCase().includes(search.toLowerCase().trim()))
  const sources = fields.source.filter(f => f.scope === scope)
  function update(rule: FieldRule) {
    setRules(old => [...old.filter(x => x.target !== rule.target || x.scope !== rule.scope), rule])
    setPreview(undefined)
  }
  function payload() {
    return rules.map(rule => {
      const text = extra[rule.scope + rule.target]
      if (text === undefined) return rule
      const value = JSON.parse(text || 'null')
      if (rule.operation === 'lookup' || rule.operation === 'enum') return { ...rule, values: value }
      if (rule.operation === 'if') return { ...rule, value: value?.equals, fallback: value?.otherwise }
      if (rule.operation === 'default') return { ...rule, fallback: value }
      return { ...rule, value }
    })
  }
  const refresh = () => cache.invalidateQueries({ queryKey: ['field-mapping', base] })
  return <div className="connect-stack">
    <Link className="back-link" to={root}>Back to mapping profiles</Link>
    <Card className="workspace-card"><h2>{profile.name}</h2><p>{profile.connectionName} · {documentKindLabels[profile.documentType]} · {directionLabels[profile.direction]}</p>
      {!!versions.length && <div className="connect-actions">{versions.slice(0, 8).map(v => <Badge key={v.number} tone={v.status === 'Active' ? 'success' : 'neutral'}>v{v.number} · {v.status}</Badge>)}</div>}
      {!!latest?.validation?.length && <ul>{latest.validation.map(x => <li key={x}>{x}</li>)}</ul>}
      {!!detail.templates.length && <div className="connect-actions">
        <Field id="mapping-template" label="Published vendor template"><Select id="mapping-template" value={template} onChange={e => setTemplate(e.target.value)}>
          <option value="">Choose a template</option>{detail.templates.map(x => <option key={x.id} value={x.id}>{x.name} · v{x.version}</option>)}</Select></Field>
        <Button variant="light" disabled={!template || actions.busy} onClick={() => void actions.run(async () => {
          await api(path + '/versions', 'POST', { expectedVersion: latest?.number ?? 0, templateId: template }); await refresh()
        })}>Copy to company</Button>
      </div>}</Card>
    <Card className="workspace-card connect-stack"><h2>Field Mapping Designer</h2>
      <div className="connect-grid">
        <Field id="mapping-scope" label="Field scope"><Select id="mapping-scope" value={scope} onChange={e => setScope(e.target.value)}><option>Header</option><option value="Line">Line items</option></Select></Field>
        <Field id="field-search" label="Search fields"><Input id="field-search" type="search" value={search} onChange={e => setSearch(e.target.value)} /></Field>
      </div>
      <details className="connect-order"><summary>Available source fields</summary><p>{sources.map(x => x.label + ' (' + x.type + ')').join(' · ')}</p></details>
      <ScrollTable name="Destination field mappings"><thead><tr><th>Destination</th><th>Source fields</th><th>Transformation</th><th>State</th></tr></thead>
        <tbody>{selected.map(field => <RuleRow key={field.scope + field.code} field={field} sources={sources}
          rule={rules.find(x => x.target === field.code && x.scope === field.scope)} operations={detail.operations} update={update}
          advanced={extra[field.scope + field.code]} setAdvanced={text => setExtra(old => ({ ...old, [field.scope + field.code]: text }))} />)}</tbody>
      </ScrollTable>
      {actions.error && <ErrorState>{actions.error}</ErrorState>}{actions.notice && <p role="status">{actions.notice}</p>}
      <div className="connect-actions">
        <Button disabled={actions.busy} onClick={() => void actions.run(async () => {
          await api(path + '/versions', 'POST', { expectedVersion: latest?.number ?? 0, rules: payload() }); await refresh()
        })}>Save mapping version</Button>
        <Button variant="light" disabled={actions.busy} onClick={() => void actions.run(async () =>
          setPreview(await api(path + '/preview', 'POST', { rules: payload() })))}>Validate mapping</Button>
        {latest && latest.status !== 'Validation Required' && latest.status !== 'Active' &&
          <Button disabled={actions.busy} onClick={() => void actions.run(async () => {
            await api(path + '/activate', 'POST', { version: latest.number }); await refresh()
          }, 'Mapping activated')}>Activate version {latest.number}</Button>}
        {profile.activeVersion && !profile.disabled && <Button variant="light" disabled={actions.busy} onClick={() => void actions.run(async () => {
          await api(path + '/disable', 'POST'); await refresh()
        })}>Disable profile</Button>}
      </div>
      <div className="connect-actions">
        {profile.direction === 'Outbound' && <Field id="preview-document" label="Committed document ID (optional)">
          <Input id="preview-document" value={document} maxLength={36} onChange={e => setDocument(e.target.value)} /></Field>}
        <Button variant="light" disabled={actions.busy} onClick={() => void actions.run(async () =>
          setPreview(await api(path + '/preview', 'POST', { rules: payload(), documentId: document || null })))}>Preview mapped payload</Button>
      </div>
    </Card>
    {preview && <Card className="workspace-card"><h2>Mapping preview</h2>
      <Badge tone={preview.valid ? 'success' : 'warning'}>{preview.valid ? 'Mapping valid' : 'Validation required'}</Badge>
      {preview.errors.length > 0 && <ul>{preview.errors.map(x => <li key={x}>{x}</li>)}</ul>}
      {preview.valid && <div className="connect-grid">{Object.entries({ Source: preview.source, 'Transformed payload': preview.payload,
        Request: preview.request, 'Expected response': preview.expectedResponse }).map(([name, data]) =>
        <details key={name} className="connect-order"><summary>{name}</summary><pre className="connect-schema">{JSON.stringify(data, null, 2)}</pre></details>)}</div>}
    </Card>}
  </div>
}

function RuleRow({ field, sources, rule, operations, update, advanced, setAdvanced }: { field: MappingField; sources: MappingField[]; rule?: FieldRule;
  operations: string[]; update: (rule: FieldRule) => void; advanced?: string; setAdvanced: (text: string) => void }) {
  const current = rule ?? { scope: field.scope, target: field.code, operation: 'omit', sources: [] }
  const disabled = field.financial || field.readOnly
  const method = current.operation
  const custom = ['constant', 'default', 'if', 'enum', 'lookup'].includes(method)
  const multivalue = method === 'concat' || method === 'object'
  const label = pretty(field.label)
  const methods = field.financialReference ? ['reference', 'omit'] :
    operations.filter(op => (op !== 'reference' || !!field.reference) && (op !== 'omit' || !field.required))
  const sourceChoices = field.financialReference ? sources.filter(source => source.code === field.code) : sources
  return <tr>
    <th scope="row">{label}<small className="mapping-hint">{field.type}{field.required ? ' · Required' : ''}</small></th>
    <td>{method === 'constant' || method === 'omit' ? '—' : <Select aria-label={label + ' source'} value={current.sources[0] ?? ''} disabled={disabled || field.financialReference}
      onChange={e => update({ ...current, sources: e.target.value ? [e.target.value, ...current.sources.slice(1)] : [] })}>
      <option value="">Choose source</option>{sourceChoices.map(s => <option key={s.code} value={s.code}>{pretty(s.label)}</option>)}</Select>}
      {multivalue && <Select aria-label={label + ' secondary source'} value={current.sources[1] ?? ''} onChange={e =>
        update({ ...current, sources: [...current.sources.slice(0, 1), ...e.target.value ? [e.target.value] : []] })}>
        <option value="">Secondary source (optional)</option>{sources.map(s => <option key={s.code} value={s.code}>{pretty(s.label)}</option>)}</Select>}</td>
    <td><Select aria-label={label + ' transformation'} value={method} disabled={disabled} onChange={e =>
      update({ ...current, operation: e.target.value, sources: ['constant', 'omit'].includes(e.target.value) ? [] : current.sources.length ? current.sources : [field.code] })}>
      {!methods.includes(method) && <option value={method}>{pretty(method)}</option>}
      {methods.map(op => <option key={op} value={op}>{pretty(op)}</option>)}</Select>
      {custom && <Input aria-label={label + ' transformation options'} placeholder={method === 'if' ? '{"equals":"A","otherwise":"B"}' : 'JSON value or lookup object'}
        value={advanced ?? JSON.stringify(method === 'if' ? { equals: current.value, otherwise: current.fallback } :
          method === 'lookup' || method === 'enum' ? current.values : method === 'default' ? current.fallback : current.value) ?? ''}
        onChange={e => setAdvanced(e.target.value)} />}</td>
    <td><Badge tone={disabled ? 'neutral' : current.operation !== 'omit' ? 'success' : 'warning'}>
      {disabled ? 'Protected' : current.operation === 'omit' ? 'Unmapped' : field.financialReference ? 'Reference lookup' : 'Mapped'}</Badge></td>
  </tr>
}

export function ReferenceMapping({ base }: { base: string }) {
  const [connection, setConnection] = useState('')
  const links = useQuery({ queryKey: ['connect', base, 'connections'], queryFn: () => api<List<Connection>>(base + '/connections'), retry: false })
  if (links.isPending) return <LoadingState label="Loading company connections…" />
  if (links.isError) return <ErrorState>{errorMessage(links.error)}</ErrorState>
  return <div className="connect-stack"><Card className="workspace-card"><h2>Reference mapping</h2><p>Link company master data to a verified identifier in the chosen connection.</p>
    <Field id="reference-connection" label="Reference connection"><Select id="reference-connection" value={connection} onChange={e => setConnection(e.target.value)}>
      <option value="">Select a connection</option>{links.data.items.filter(c => c.status === 'Connected').map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
    </Select></Field></Card>{connection && <ReferenceForm key={connection} base={base + '/connections/' + connection + '/references'} />}</div>
}

function ReferenceForm({ base }: { base: string }) {
  const [kind, setKind] = useState('Customer')
  const [local, setLocal] = useState('')
  const [external, setExternal] = useState('')
  const actions = useConnectAction()
  const result = useQuery({ queryKey: ['reference-mapping', base, kind], queryFn: () => api<ReferenceChoices>(base + '?kind=' + kind), retry: false })
  if (result.isPending) return <LoadingState />
  if (result.isError) return <ErrorState>{errorMessage(result.error)}</ErrorState>
  const data = result.data
  const locals = kind === 'Currency' ? [{ id: 'PHP', code: 'PHP', name: 'Philippine peso' }, ...data.local] : data.local
  function save(event: FormEvent) {
    event.preventDefault()
    void actions.run(async () => {
      await api(base, 'PUT', { kind, localKey: local, externalId: external })
      await result.refetch()
    }, 'Reference mapping saved')
  }
  return <Card className="workspace-card connect-stack"><h3>Company and external identities</h3>
    <form className="connect-stack" onSubmit={save}><div className="connect-grid">
      <Field id="ref-kind" label="Reference type"><Select id="ref-kind" value={kind} onChange={e => { setKind(e.target.value); setLocal(''); setExternal('') }}>
        {data.kinds.map(x => <option key={x} value={x}>{pretty(x)}</option>)}</Select></Field>
      <Field id="ref-local" label="Company reference"><Select id="ref-local" value={local} required onChange={e => setLocal(e.target.value)}>
        <option value="">Choose a local identity</option>{locals.map(x => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}</Select></Field>
      <Field id="ref-external" label="External identifier"><Select id="ref-external" value={external} required onChange={e => setExternal(e.target.value)}>
        <option value="">Choose an external identity</option>{data.choices.map(x => <option key={x.externalId} value={x.externalId}>{x.externalCode}</option>)}</Select></Field>
    </div><div className="connect-actions"><Button type="submit" disabled={actions.busy || !local || !external}>Save reference mapping</Button>
      <Button type="button" variant="light" onClick={() => void result.refetch()}>Refresh reference choices</Button></div>
      {actions.error && <ErrorState>{actions.error}</ErrorState>}{actions.notice && <p role="status">{actions.notice}</p>}</form>
    <ScrollTable name="Reference mapping register"><thead><tr><th>Company code</th><th>External ID</th><th>State</th></tr></thead>
      <tbody>{locals.map(x => {
        const saved = data.items.find(m => m.localKey === x.id)
        const suggested = data.suggestions.find(m => m.id === x.id)
        return <tr key={x.id}><td>{x.code}</td><td>{saved?.externalId ?? '—'}</td><td>{saved ? 'Mapped' : suggested ?
          <Button type="button" variant="light" onClick={() => { setLocal(x.id); setExternal(suggested.externalId) }}>Review suggested match</Button> : 'Unmapped'}</td></tr>
      })}</tbody>
    </ScrollTable>
  </Card>
}

