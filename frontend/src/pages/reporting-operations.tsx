import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Badge, Button, Card, ErrorState, Field, Input, LoadingState, Select } from '@/components/ui'
import { api, errorMessage } from '@/lib/account-api'

type Operation = { id: string; documentId: string; mode: string; state: string; attempts: number; failure?: string;
  payloadHash?: string; connectorVersion?: number; schemaVersion: string; batchId?: string }
type List = { items: Operation[]; activeConnector?: number; scenarios: string[]; canPrepare: boolean }
type Attempt = { sequence: number; action: string; outcome: string; reference?: string; responseHash: string; at: string }
const title = (s: string) => s.replace(/([a-z])([A-Z])/g, '$1 $2')

export function ReportingOperationsPanel({ base, documents }: { base: string; documents: { id: string; number: string; developmentFixture: boolean }[] }) {
  const path = base + '/operations'
  const [mode, setMode] = useState('Preparation')
  const [selected, setSelected] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const query = useQuery({ queryKey: [path], queryFn: () => api<List>(path), refetchInterval: 3500, retry: false })
  const history = useQuery({ queryKey: [path, selected], queryFn: () => api<{ items: Attempt[] }>(path + '/' + selected + '/attempts'),
    enabled: !!selected, refetchInterval: 3500, retry: false })
  async function run(command: () => Promise<unknown>) {
    setBusy(true); setError('')
    try { await command(); await query.refetch() }
    catch (e) { setError(errorMessage(e)) }
    finally { setBusy(false) }
  }
  function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    const ids = form.getAll('document')
    if (!ids.length) { setError('Select at least one document.'); return }
    void run(() => api(path, 'POST', { documents: ids, mode, scenario: form.get('scenario'),
      connectorVersion: mode === 'DevelopmentSimulation' ? query.data?.activeConnector : null }))
  }
  return <Card className="workspace-card workspace-stack">
    <h2>Reporting operations</h2>
    <p>Intact committed evidence can be prepared or used in a development simulation. Government status stays Not submitted.</p>
    {error && <ErrorState>{error}</ErrorState>}
    {query.isPending ? <LoadingState /> : query.isError ? <ErrorState>{errorMessage(query.error)}</ErrorState> : <>
      {query.data.canPrepare && documents.length > 0 && <form className="workspace-form" onSubmit={submit}>
        <Field id="report-op-mode" label="Operating mode"><Select id="report-op-mode" value={mode} onChange={e => setMode(e.target.value)}>
          <option value="Preparation">Preparation</option>
          {query.data.activeConnector != null && <option value="DevelopmentSimulation">Development simulation</option>}
        </Select></Field>
        <fieldset className="connect-fieldset"><legend>Explicit document selection</legend>
          {documents.filter(d => mode === 'Preparation' || d.developmentFixture).slice(0, 20).map(d =>
            <label className="connect-check" key={d.id}><input type="checkbox" name="document" value={d.id} />{d.number}</label>)}
        </fieldset>
        {mode === 'DevelopmentSimulation' && <Field id="report-op-scenario" label="Fictional outcome">
          <Select id="report-op-scenario" name="scenario">{query.data.scenarios.map(s => <option key={s} value={s}>{title(s)}</option>)}</Select>
        </Field>}
        <Button type="submit" disabled={busy}>Queue reporting operation</Button>
      </form>}
      {query.data.items.length ? <div className="table-scroll" role="region" aria-label="Reporting operation register" tabIndex={0}>
        <table className="workspace-table"><thead><tr><th>Operation</th><th>Mode</th><th>State</th><th>Pins</th><th>Attempts</th></tr></thead>
          <tbody>{query.data.items.map(row => <tr key={row.id}><td><Button variant="light" onClick={() => setSelected(row.id)}>
            {documents.find(d => d.id === row.documentId)?.number ?? row.documentId.slice(0, 8)}</Button></td>
            <td>{title(row.mode)}</td><td><Badge>{title(row.state)}</Badge>{row.failure && <p>{title(row.failure)}</p>}</td>
            <td>{row.schemaVersion}{row.connectorVersion != null && ' · Connector v' + row.connectorVersion}</td><td>{row.attempts}</td></tr>)}</tbody>
        </table>
      </div> : <p>No reporting operations.</p>}
    </>}
    {selected && <section className="workspace-stack" aria-label="Operation attempt history">
      <h3>Operation {selected.slice(0, 8)}</h3>
      {query.data?.items.find(x => x.id === selected)?.payloadHash && <p>Payload SHA-256 {query.data.items.find(x => x.id === selected)?.payloadHash}</p>}
      {history.isError && <ErrorState>{errorMessage(history.error)}</ErrorState>}
      {history.isPending && <LoadingState />}
      {history.data?.items.map(e => <div key={e.sequence}><p>{title(e.action)} · {title(e.outcome)} · {e.at}</p>
        {e.reference && <p>Simulated reference {e.reference}</p>}<p>Response SHA-256 {e.responseHash}</p></div>)}
      {query.data?.canPrepare && query.data.items.filter(x => x.id === selected).map(row => {
        const verify = row.state === 'UncertainOutcome'
        if (!verify && (row.state !== 'Queued' || row.attempts > 0)) return null
        return <form key={row.id} className="connect-actions" onSubmit={e => {
          e.preventDefault()
          const reason = new FormData(e.currentTarget).get('reason')
          void run(() => api(path + '/' + row.id + (verify ? '/verify' : '/cancel'), 'POST', { reason }))
        }}>
          <Field id="report-op-reason" label="Reason"><Input id="report-op-reason" name="reason" maxLength={600} required /></Field>
          <Button type="submit" variant="light" disabled={busy}>{verify ? 'Verify simulated status' : 'Cancel unstarted job'}</Button>
        </form>
      })}
    </section>}
  </Card>
}
