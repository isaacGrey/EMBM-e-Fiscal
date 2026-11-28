import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button, Card, ErrorState, Field, Input, LoadingState, Select } from '@/components/ui'
import { api, errorMessage } from '@/lib/account-api'

const base = '/vendor/bir-connector'
type Version = { number: number; environment: string; apiVersion: string; documentation?: string; origin?: string
  authenticationAdapter: string; specification?: { format: string; title: string; endpoints: { method: string; path: string; request?: unknown; response?: unknown }[] } }
type Event = { version: number; sequence: number; state: string; reason: string }
type Trial = { id: string; version: number; scenario: string; payloadHash: string; responseHash: string; response: unknown }
type Register = { queue: { mode: string; state: string; count: number }[]; versions: Version[]; events: Event[]; tests: Trial[]; scenarios: string[]; grants: string[]; mappingVersion: string
  governance: { incidents: { state: string; count: number }[]; openHolds: number; exports: number } }
const next: Record<string, string[]> = {
  Draft: ['UnderReview'], UnderReview: ['ConfigurationValidated'], ConfigurationValidated: ['ApprovedForActivation'],
  ApprovedForActivation: ['Active'], Active: ['Suspended', 'Disabled'], Suspended: ['Active', 'Disabled'], Disabled: ['Retired'],
}
const title = (s: string) => s.replace(/([a-z])([A-Z])/g, '$1 $2')

export function VendorBirConnector() {
  const [mode, setMode] = useState('DevelopmentSimulation')
  const [chosen, setChosen] = useState<number>()
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [preview, setPreview] = useState<unknown>()
  const data = useQuery({ queryKey: [base], queryFn: () => api<Register>(base), retry: false })
  async function run(command: () => Promise<unknown>) {
    setBusy(true); setError('')
    try { await command(); await data.refetch() }
    catch (e) { setError(errorMessage(e)) }
    finally { setBusy(false) }
  }
  function save(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    const metadata = { environment: mode, apiVersion: form.get('version'), documentation: form.get('documentation') || null,
      origin: mode === 'DevelopmentSimulation' ? 'https://bir-simulator.example.test' : null,
      timeoutSeconds: Number(form.get('timeout')), attempts: Number(form.get('attempts')), rateLimit: Number(form.get('rate')),
      batch: form.get('batch') === 'on', expectedVersion: data.data?.versions[0]?.number ?? 0 }
    void run(async () => {
      const manual = String(form.get('manual') ?? '').trim()
      const body = new FormData()
      body.set('metadata', JSON.stringify({ ...metadata, manual: manual ? JSON.parse(manual) : null }))
      const file = form.get('specification')
      if (file instanceof File && file.size > 0) body.set('specification', file)
      const result = await api<Version>(base, 'POST', body)
      setChosen(result.number)
    })
  }
  if (data.isPending) return <LoadingState />
  if (data.isError) return <ErrorState>{errorMessage(data.error)}</ErrorState>
  const version = data.data.versions.find(v => v.number === chosen) ?? data.data.versions[0]
  const current = data.data.events.find(e => e.version === version?.number)
  const grants = data.data.grants
  return <Card className="workspace-card workspace-stack">
    <h2>BIR API configuration</h2>
    <section aria-label="Aggregate reporting queue">
      <h3>Reporting queue monitoring</h3>
      {data.data.queue.length === 0 ? <p>No reporting operations recorded.</p> : <dl className="workspace-definitions">{data.data.queue.map(row =>
        <div key={row.mode + ':' + row.state}><dt>{title(row.mode)} · {title(row.state)}</dt><dd>{row.count}</dd></div>)}</dl>}
    </section>
    <section aria-label="Aggregate development governance">
      <h3>Development records and continuity</h3>
      <dl className="workspace-definitions">
        {data.data.governance.incidents.map(row => <div key={row.state}><dt>{title(row.state)} incidents</dt><dd>{row.count}</dd></div>)}
        <div><dt>Open legal holds</dt><dd>{data.data.governance.openHolds}</dd></div>
        <div><dt>Generated taxpayer exports</dt><dd>{data.data.governance.exports}</dd></div>
      </dl>
    </section>
    <p>Official specifications and connectivity are pending. All test requests run in the isolated development simulator.</p>
    <p>Regulatory mapping {data.data.mappingVersion}</p>
    {error && <ErrorState>{error}</ErrorState>}
    {grants.includes('Vendor.BirConnector.Configure') && <details><summary>Record new configuration version</summary>
      <form className="workspace-form" onSubmit={save}>
        <Field id="bir-mode" label="Environment"><Select id="bir-mode" value={mode} onChange={e => setMode(e.target.value)}>
          <option value="DevelopmentSimulation">Development simulation</option><option value="OfficialPending">Awaiting official specifications</option>
        </Select></Field>
        <Field id="bir-version" label="API version"><Input id="bir-version" name="version" required maxLength={50} /></Field>
        <Field id="bir-docs" label="Documentation URL (optional)"><Input id="bir-docs" name="documentation" type="url" /></Field>
        {mode === 'DevelopmentSimulation' && <>
          <Field id="bir-file" label="JSON or YAML specification"><Input id="bir-file" name="specification" type="file" accept=".json,.yaml,.yml" /></Field>
          <Field id="bir-manual" label="Manual configuration JSON"><textarea id="bir-manual" name="manual" rows={6} /></Field>
        </>}
        <div className="connect-grid">
          <Field id="bir-timeout" label="Timeout in seconds"><Input id="bir-timeout" name="timeout" type="number" defaultValue={10} min={1} max={30} required /></Field>
          <Field id="bir-attempts" label="Attempt limit"><Input id="bir-attempts" name="attempts" type="number" defaultValue={3} min={1} max={5} required /></Field>
          <Field id="bir-rate" label="Operations per minute"><Input id="bir-rate" name="rate" type="number" defaultValue={10} min={1} max={60} required /></Field>
        </div>
        <label><input type="checkbox" name="batch" /> Enable synthetic batch outcomes</label>
        <Button type="submit" disabled={busy}>Record configuration</Button>
      </form>
    </details>}
    {version && current && <>
      <Field id="bir-history" label="Connector version"><Select id="bir-history" value={version.number}
        onChange={e => { setChosen(Number(e.target.value)); setPreview(undefined) }}>
        {data.data.versions.map(v => <option key={v.number} value={v.number}>v{v.number} · {title(v.environment)} · {v.apiVersion}</option>)}
      </Select></Field>
      <p>{title(current.state)} · {title(version.authenticationAdapter)}{version.origin && ' · ' + version.origin}</p>
      {version.specification?.endpoints.map(op => <details key={op.method + op.path}><summary>{op.method} {op.path}</summary>
        <h4>Request schema</h4><pre className="doc-preview-mails">{JSON.stringify(op.request, null, 2)}</pre>
        <h4>Response schema</h4><pre className="doc-preview-mails">{JSON.stringify(op.response, null, 2)}</pre>
      </details>)}
      {version.environment === 'DevelopmentSimulation' && (next[current.state] ?? []).filter(state =>
        grants.includes(['Active', 'Suspended', 'Disabled', 'Retired'].includes(state) ? 'Vendor.BirConnector.Activate' : 'Vendor.BirConnector.Review'),
      ).map(state => <form key={state} className="connect-actions" onSubmit={e => {
        e.preventDefault()
        const reason = new FormData(e.currentTarget).get('reason')
        void run(() => api(base + '/' + version.number + '/transitions', 'POST', { state, expectedSequence: current.sequence, reason }))
      }}>
        <Field id={'bir-reason-' + state} label={title(state) + ' reason'}><Input id={'bir-reason-' + state} name="reason" maxLength={500} required /></Field>
        <Button type="submit" variant="light" disabled={busy}>{title(state)}</Button>
      </form>)}
      {grants.includes('Vendor.BirConnector.Test') && ['ConfigurationValidated', 'ApprovedForActivation', 'Active'].includes(current.state) &&
        <form className="workspace-form" onSubmit={e => {
          e.preventDefault()
          const scenario = new FormData(e.currentTarget).get('scenario')
          void run(async () => setPreview(await api(base + '/' + version.number + '/tests', 'POST', { scenario })))
        }}>
          <Field id="bir-scenario" label="Fictional response"><Select id="bir-scenario" name="scenario">
            {data.data.scenarios.map(s => <option key={s} value={s}>{title(s)}</option>)}
          </Select></Field><Button type="submit" disabled={busy}>Run in-process test</Button>
        </form>}
      {preview != null && <pre className="doc-preview-mails" aria-label="Simulated request and response">{JSON.stringify(preview, null, 2)}</pre>}
      <details><summary>Version events</summary>{data.data.events.filter(e => e.version === version.number).map(e => <p key={e.sequence}>{title(e.state)} · {e.reason}</p>)}</details>
      <details><summary>Test history</summary>{data.data.tests.filter(t => t.version === version.number).map(t => <details key={t.id}>
        <summary>{title(t.scenario)} · {t.id.slice(0, 8)}</summary><p>Payload SHA-256 {t.payloadHash}</p><p>Response SHA-256 {t.responseHash}</p>
        <pre className="doc-preview-mails">{JSON.stringify(t.response, null, 2)}</pre>
      </details>)}</details>
    </>}
  </Card>
}
