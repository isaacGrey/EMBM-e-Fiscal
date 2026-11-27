import { useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Card, ErrorState, Field, Input, LoadingState, Select } from '@/components/ui'
import { api, errorMessage, manilaTimestamp } from '@/lib/account-api'
import { status, typeNames, type Coverage, type CoverageList, type CoveragePreview, type ReconcileType } from '@/lib/reconciliation-api'
import { ScrollTable } from './connect'
import { useConnectAction } from './field-mapping'

export function ReconciliationCoverage({ base, initialConnection }: { base: string; initialConnection?: string }) {
  const query = useQuery({ queryKey: ['reconciliation', base, 'coverage'], queryFn: () => api<CoverageList>(base + '/coverage'), retry: false })
  const [selected, setSelected] = useState(initialConnection ?? '')
  if (query.isPending) return <LoadingState label="Loading accounting coverage…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  const link = query.data.links.find(x => x.id === selected)
  const history = query.data.items.filter(x => x.connectionId === selected)
  return <div className="connect-stack">
    <Card className="workspace-card">
      <h2>Reconciliation coverage</h2>
      <div className="connect-grid"><Field id="coverage-connection" label="Accounting connection">
        <Select id="coverage-connection" value={selected} onChange={e => setSelected(e.target.value)}>
          <option value="">Choose an accounting connection</option>
          {query.data.links.map(x => <option key={x.id} value={x.id}>{x.name} · {x.externalResourceName}</option>)}
        </Select>
      </Field></div>
      {link && <p>External company file {link.externalResourceName} · {status(link.status)}</p>}
      {!query.data.links.length && <p>Connect a development accounting company file to configure coverage.</p>}
    </Card>
    {link && query.data.canManage && <CoverageEditor key={link.id + ':' + history[0]?.number} base={base} selected={link.id} history={history} />}
    {link && <Card className="workspace-card"><h2>Coverage history</h2>
      <ScrollTable name="Coverage versions"><thead><tr>{['Version', 'Coverage period', 'Mode', 'Reason', 'Effective date', 'State'].map(h => <th key={h}>{h}</th>)}</tr></thead>
        <tbody>{history.map(v => <tr key={v.id}><td>v{v.number}</td><td>{v.start} to {v.end ?? 'open'}</td><td>{status(v.mode)}</td><td>{v.reason}</td>
          <td>{v.effectiveAt ? manilaTimestamp.format(new Date(v.effectiveAt)) : '—'}</td><td>{v.active ? 'Active' : v.effectiveAt ? 'Approved' : 'Draft'}</td></tr>)}</tbody>
      </ScrollTable>{!history.length && <p>No coverage versions are recorded for this connection.</p>}
    </Card>}
  </div>
}

function CoverageEditor({ base, selected, history }: { base: string; selected: string; history: Coverage[] }) {
  const cache = useQueryClient()
  const latest = history[0]
  const [mode, setMode] = useState(latest?.mode ?? 'EfiscalOnly')
  const [exclude, setExclude] = useState(latest?.excludedSources.includes('ApprovedExternal') ?? false)
  const [preview, setPreview] = useState<CoveragePreview>()
  const [confirmed, setConfirmed] = useState(false)
  const [effective, setEffective] = useState('')
  const actions = useConnectAction()
  const refresh = () => cache.invalidateQueries({ queryKey: ['reconciliation', base] })
  const draft = latest && !latest.effectiveAt ? latest : undefined
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const values = new FormData(event.currentTarget)
    const submitter = (event.nativeEvent as SubmitEvent).submitter as HTMLButtonElement | null
    const previewOnly = submitter?.value === 'preview'
    const input = {
      connectionId: selected, adoption: values.get('adoption'), start: values.get('start') || null, end: values.get('end') || null, mode,
      types: values.getAll('type'), reason: values.get('reason'), issuanceScope: values.get('scope'), excludedSources: exclude ? ['ApprovedExternal'] : [],
      exclusionReason: values.get('exclusionReason'), timeZone: 'Asia/Manila', expectedVersion: latest?.number ?? 0,
      amountTolerance: Number(values.get('tolerance')), freshnessHours: Number(values.get('freshness')), intervalHours: Number(values.get('interval')) || null,
      batchSize: Number(values.get('batch')), afterSynchronization: values.has('after'),
    }
    void actions.run(async () => {
      if (previewOnly) setPreview(await api<CoveragePreview>(base + '/coverage/preview', 'POST', input))
      else { await api(base + '/coverage', 'POST', input); await refresh() }
    }, previewOnly ? 'Coverage preview updated' : 'Coverage draft saved')
  }
  return <Card className="workspace-card connect-stack">
    <h2>Coverage configuration</h2>
    <form className="connect-stack" onSubmit={submit}>
      <div className="connect-grid">
        <Field id="coverage-adoption" label="Intended eFiscal adoption date"><Input id="coverage-adoption" name="adoption" type="date" defaultValue={latest?.adoption} required /></Field>
        <Field id="coverage-start" label="Reconciliation start date"><Input id="coverage-start" name="start" type="date" defaultValue={latest?.start} /></Field>
        <Field id="coverage-end" label="Reconciliation end date"><Input id="coverage-end" name="end" type="date" defaultValue={latest?.end} /></Field>
        <Field id="coverage-mode" label="Coverage mode"><Select id="coverage-mode" value={mode} onChange={e => { setMode(e.target.value); setConfirmed(false) }}>
          <option value="EfiscalOnly">eFiscal-only</option><option value="FullCoverage">Full coverage</option></Select></Field>
        <Field id="coverage-scope" label="Approved issuance scope"><Select id="coverage-scope" name="scope" defaultValue={latest?.issuanceScope ?? 'Adopted'}>
          <option value="Adopted">Adopted issuance workflow</option><option value="AllVerifiedSources">All verified development sources</option></Select></Field>
        <Field id="coverage-reason" label="Configuration reason"><Input id="coverage-reason" name="reason" maxLength={300} defaultValue={latest?.reason} required /></Field>
      </div>
      <fieldset className="connect-fieldset"><legend>Included document types</legend><div className="connect-actions">
        {Object.entries(typeNames).map(([type, name]) => <label className="connect-check" key={type}><input type="checkbox" name="type" value={type}
          defaultChecked={latest?.types.includes(type as ReconcileType) ?? type !== 'SalesOrder'} />{name}</label>)}
      </div></fieldset>
      <label className="connect-check"><input type="checkbox" checked={exclude} onChange={e => setExclude(e.target.checked)} />Exclude approved external issuance</label>
      {exclude && <Field id="coverage-exclusion" label="Exclusion reason"><Input id="coverage-exclusion" name="exclusionReason" maxLength={300} defaultValue={latest?.exclusionReason} required /></Field>}
      <div className="connect-grid">
        <Field id="coverage-zone" label="Business timezone"><Input id="coverage-zone" value="Asia/Manila" readOnly /></Field>
        <Field id="coverage-tolerance" label="Comparison policy"><Select id="coverage-tolerance" name="tolerance" defaultValue={latest?.amountTolerance ?? 0}>
          <option value="0">Exact comparison</option><option value="0.01">Development PHP rounding within 0.01</option></Select></Field>
        <Field id="coverage-frequency" label="Schedule"><Select id="coverage-frequency" name="interval" defaultValue={latest?.intervalHours ?? 0}>
          <option value="0">Manual</option><option value="24">Daily</option><option value="168">Weekly</option></Select></Field>
        <Field id="coverage-freshness" label="Verification freshness in hours"><Input id="coverage-freshness" name="freshness" type="number" min={1} max={720} defaultValue={latest?.freshnessHours ?? 48} required /></Field>
        <Field id="coverage-batch" label="Maximum batch size"><Input id="coverage-batch" name="batch" type="number" min={1} max={50} defaultValue={latest?.batchSize ?? 25} required /></Field>
      </div>
      <label className="connect-check"><input type="checkbox" name="after" defaultChecked={latest?.afterSynchronization ?? false} />Reconcile after confirmed synchronization</label>
      <p>Transactions created before your approved reconciliation start date will be excluded from ordinary historical missing-document detection. Your accounting records will not be deleted or modified.</p>
      {mode === 'FullCoverage' && <p className="reconciliation-warning">Eligible accounting transactions created within the selected coverage period that do not have an expected corresponding EMBM eFiscal document may be reported as missing eFiscal records.</p>}
      <div className="connect-actions"><Button type="submit" variant="light" value="preview" disabled={actions.busy}>Preview coverage</Button>
        <Button type="submit" value="save" disabled={actions.busy}>Save coverage draft</Button></div>
    </form>
    {preview && <dl className="workspace-definitions"><div><dt>Coverage preview</dt><dd>{preview.valid ? 'No overlap found' : 'Conflicting connection coverage'}</dd></div>
      <div><dt>Committed documents retain destination</dt><dd>{preview.pinnedCommitted}</dd></div>
      <div><dt>Prior checks with findings</dt><dd>{preview.priorChecksWithFindings}</dd></div>
      <div><dt>Discovered historical records</dt><dd>{preview.knownHistory ?? 'Unavailable'}</dd></div></dl>}
    {draft && <div className="connect-stack">
      <h3>Activate draft v{draft.number}</h3><p>{draft.start} to {draft.end ?? 'open'} · {status(draft.mode)}</p>
      {draft.mode === 'FullCoverage' && mode !== 'FullCoverage' && <p className="reconciliation-warning">Eligible accounting records in this period may be reported as missing eFiscal records.</p>}
      <Field id="coverage-effective" label="Effective timestamp (optional)"><Input id="coverage-effective" type="datetime-local" value={effective} onChange={e => setEffective(e.target.value)} /></Field>
      <label className="connect-check"><input type="checkbox" checked={confirmed} onChange={e => setConfirmed(e.target.checked)} />I confirm this coverage period and issuance scope</label>
      <div><Button type="button" disabled={!confirmed || actions.busy} onClick={() => void actions.run(async () => {
        await api(base + '/coverage/' + draft.id + '/activate', 'POST', { confirm: confirmed, effectiveAt: effective ? new Date(effective).toISOString() : null })
        await refresh()
      }, 'Coverage activated')}>Activate coverage</Button></div>
    </div>}
    {actions.error && <ErrorState>{actions.error}</ErrorState>}{actions.notice && <p role="status">{actions.notice}</p>}
  </Card>
}

