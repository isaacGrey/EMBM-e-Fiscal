import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button, Card, ErrorState, Field, Input, LoadingState, Select } from '@/components/ui'
import { api, apiBlob, errorMessage, manilaTimestamp, type Branch } from '@/lib/account-api'
import { ScrollTable } from './connect'

type Archive = { id: string; from: string; through: string; documentCount: number; generatedAt: string; sha256: string }
type Period = { from: string; through: string }

function DateRange({ prefix }: { prefix: string }) {
  return <div className="connect-grid">
    <Field id={prefix + '-from'} label="From business date"><Input id={prefix + '-from'} name="from" type="date" required /></Field>
    <Field id={prefix + '-through'} label="Through business date"><Input id={prefix + '-through'} name="through" type="date" required /></Field>
  </div>
}

export function TaxpayerPortability({ base }: { base: string }) {
  const archive = useQuery({ queryKey: ['portability', base], queryFn: () => api<{ items: Archive[] }>(base) })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  async function generate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const dates = Object.fromEntries(new FormData(event.currentTarget))
    setBusy(true); setError(''); setMessage('')
    try {
      await api(base, 'POST', dates)
      await archive.refetch()
      setMessage('Export ready')
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  async function download(row: Archive) {
    setError('')
    try {
      const url = URL.createObjectURL(await apiBlob(base + '/' + row.id + '/archive', 'GET'))
      const link = document.createElement('a')
      link.href = url
      link.download = 'efiscal-export-' + row.id + '.zip'
      link.click()
      setTimeout(() => URL.revokeObjectURL(url), 1000)
    } catch (failure) { setError(errorMessage(failure)) }
  }
  if (archive.isPending) return <LoadingState label="Loading export history…" />
  if (archive.isError) return <ErrorState>{errorMessage(archive.error)}</ErrorState>
  return <Card className="workspace-card connect-stack taxpayer-exports">
    <h2>Taxpayer record exports</h2>
    <p>Export up to 100 documents from an inclusive business date range of one year. The ZIP includes a JSON manifest and SHA-256 checksums.</p>
    <form className="connect-stack" onSubmit={event => void generate(event)}>
      <DateRange prefix="portability" />
      <div><Button type="submit" disabled={busy}>{busy ? 'Generating…' : 'Generate export'}</Button></div>
    </form>
    {message && <p role="status">{message}</p>}{error && <ErrorState>{error}</ErrorState>}
    {archive.data.items.length === 0 ? <p>No exports have been generated for this company.</p> : <ScrollTable name="Taxpayer export history">
      <thead><tr><th>Coverage</th><th>Documents</th><th>Generated</th><th>Archive</th></tr></thead>
      <tbody>{archive.data.items.map(row => <tr key={row.id}>
        <td>{row.from} through {row.through}</td><td>{row.documentCount}</td>
        <td>{manilaTimestamp.format(new Date(row.generatedAt))}</td><td><Button variant="light" onClick={() => void download(row)}>Download ZIP</Button></td>
      </tr>)}</tbody>
    </ScrollTable>}
  </Card>
}

type Preservation = {
  categories: string[]; canManage: boolean; canRelease: boolean;
  holds: { hold: Period & { id: string; category: string; reason: string; branchId?: string }; release?: { reason: string } }[]
  policies: { category: string; version: number; effectiveFrom: string; proposedYears?: number; preservation: string }[]
}

export function TaxpayerPreservation({ base, branches }: { base: string; branches: Branch[] }) {
  const register = useQuery({ queryKey: ['preservation', base], queryFn: () => api<Preservation>(base) })
  const [releaseId, setReleaseId] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  async function save(event: FormEvent<HTMLFormElement>, path: string, transform: (data: Record<string, FormDataEntryValue>) => object) {
    event.preventDefault()
    const form = event.currentTarget
    setBusy(true); setError('')
    try {
      await api(base + path, 'POST', transform(Object.fromEntries(new FormData(form))))
      await register.refetch()
      form.reset(); setReleaseId('')
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  if (register.isPending) return <LoadingState label="Loading preservation register…" />
  if (register.isError) return <ErrorState>{errorMessage(register.error)}</ErrorState>
  const { categories, holds, policies, canManage, canRelease } = register.data
  return <Card className="workspace-card connect-stack">
    <h2>Record preservation</h2>
    <p>Preserve all records. Any retention period requires verified legal review before disposal can be authorized.</p>
    {error && <ErrorState>{error}</ErrorState>}
    {canManage && <form className="connect-stack" onSubmit={event => void save(event, '/holds', data => ({ ...data, branchId: data.branchId || null }))}>
      <h3>Place legal hold</h3>
      <div className="connect-grid">
        <Field id="hold-category" label="Record category"><Select id="hold-category" name="category">{categories.map(value => <option key={value}>{value}</option>)}</Select></Field>
        <Field id="hold-branch" label="Branch scope"><Select id="hold-branch" name="branchId"><option value="">All company branches</option>
          {branches.map(branch => <option key={branch.id} value={branch.id}>{branch.name}</option>)}</Select></Field>
      </div>
      <DateRange prefix="legal-hold" />
      <Field id="hold-reason" label="Hold reason"><Input id="hold-reason" name="reason" required maxLength={1000} /></Field>
      <div><Button type="submit" disabled={busy}>Place hold</Button></div>
    </form>}
    {holds.length === 0 ? <p>No legal holds have been recorded.</p> : <ScrollTable name="Legal holds">
      <thead><tr><th>Category</th><th>Scope</th><th>Period</th><th>Status</th><th>Reason</th></tr></thead>
      <tbody>{holds.map(({ hold, release }) => <tr key={hold.id}>
        <td>{hold.category}</td><td>{hold.branchId ? branches.find(x => x.id === hold.branchId)?.name ?? 'Recorded branch' : 'All company branches'}</td>
        <td>{hold.from} through {hold.through}</td><td>{release ? 'Released' : 'Active'}</td>
        <td>{release ? release.reason : hold.reason}
          {!release && canRelease && <div><Button variant="light" onClick={() => setReleaseId(hold.id)}>Record release</Button></div>}</td>
      </tr>)}</tbody></ScrollTable>}
    {releaseId && <form className="connect-stack" onSubmit={event => void save(event, '/holds/' + releaseId + '/release', data => data)}>
      <Field id="release-reason" label="Release authorization and reason"><Input id="release-reason" name="reason" required maxLength={1000} /></Field>
      <div className="connect-actions"><Button type="submit" disabled={busy}>Release hold</Button>
        <Button variant="light" onClick={() => setReleaseId('')}>Cancel</Button></div>
    </form>}
    <h3>Retention policy history</h3>
    {canRelease && <form className="connect-stack" onSubmit={event => void save(event, '/policies', data => ({
      ...data, proposedYears: data.proposedYears ? Number(data.proposedYears) : null,
      expectedVersion: Math.max(0, ...policies.filter(x => x.category === data.category).map(x => x.version)),
    }))}>
      <div className="connect-grid">
        <Field id="retention-category" label="Retention category"><Select id="retention-category" name="category">{categories.map(value => <option key={value}>{value}</option>)}</Select></Field>
        <Field id="retention-from" label="Effective from"><Input id="retention-from" name="effectiveFrom" type="date" required /></Field>
        <Field id="retention-years" label="Proposed years"><Input id="retention-years" name="proposedYears" type="number" min={1} max={100} /></Field>
      </div>
      <Field id="retention-notes" label="Preservation requirements"><Input id="retention-notes" name="preservation" required maxLength={1000} /></Field>
      <Field id="retention-migration" label="Migration evidence reference"><Input id="retention-migration" name="migrationEvidence" maxLength={500} /></Field>
      <div><Button type="submit" disabled={busy}>Record policy version</Button></div>
    </form>}
    {policies.length === 0 ? <p>No retention policy versions have been recorded.</p> : <ul>{policies.map(row =>
      <li key={row.category + row.version}>{row.category} · v{row.version} · {row.effectiveFrom} · {row.preservation}</li>)}</ul>}
  </Card>
}

type ContinuityEntry = { kind: string; key: string; version: number; branchId?: string; recordedAt: string;
  details: { id?: string; state?: string; startedAt?: string; restoredAt?: string; notes?: string; eligibility?: string;
    components?: string[]; reportingOperationIds?: string[]; evidenceReference?: string;
    original?: { series: string; number: string; date: string; reviewNotes: string } } }
type Continuity = { entries: ContinuityEntry[]; canManage: boolean }
const localTime = (value: string) => {
  const date = new Date(value)
  return new Date(date.valueOf() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}

export function ReportingContinuity({ base, branches }: { base: string; branches: Branch[] }) {
  const register = useQuery({ queryKey: ['continuity', base], queryFn: () => api<Continuity>(base) })
  const [edit, setEdit] = useState<ContinuityEntry>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  async function save(event: FormEvent<HTMLFormElement>, kind: 'incidents' | 'manual-invoices') {
    event.preventDefault()
    const form = event.currentTarget
    const data = new FormData(form)
    const fields = Object.fromEntries(data)
    const payload = kind === 'manual-invoices' ? { ...fields, existingDocumentId: fields.existingDocumentId || null }
      : { ...fields, id: edit?.details.id, branchId: edit ? edit.branchId ?? null : fields.branchId || null,
        startedAt: edit?.details.startedAt ?? new Date(String(fields.startedAt)).toISOString(),
        restoredAt: fields.restoredAt ? new Date(String(fields.restoredAt)).toISOString() : null,
        components: data.getAll('components'), reportingOperationIds: String(fields.reportingOperationIds ?? '').split(/[,\s]+/).filter(Boolean),
        expectedVersion: edit?.version ?? 0 }
    setBusy(true); setError('')
    try { await api(base + '/' + kind, 'POST', payload); await register.refetch(); form.reset(); setEdit(undefined) }
    catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  if (register.isPending) return <LoadingState label="Loading reporting continuity…" />
  if (register.isError) return <ErrorState>{errorMessage(register.error)}</ErrorState>
  const entries = register.data.entries
  const incidents = entries.filter(x => x.kind === 'Incident')
  const current = incidents.filter(x => !incidents.some(other => other.key === x.key && other.version > x.version))
  return <Card className="workspace-card connect-stack">
    <h2>Outage and recovery records</h2>
    <p>Recovery deadline applicability requires verified regulatory review. A manual invoice reference records evidence for review.</p>
    {error && <ErrorState>{error}</ErrorState>}
    {register.data.canManage && <form className="connect-stack" key={edit?.key ?? 'new'} onSubmit={event => void save(event, 'incidents')}>
      <h3>{edit ? 'Record incident revision' : 'Record incident'}</h3>
      <div className="connect-grid">
        <Field id="incident-branch" label="Affected branch"><Select id="incident-branch" name="branchId" defaultValue={edit?.branchId ?? ''} disabled={!!edit}>
          <option value="">All company branches</option>{branches.map(x => <option value={x.id} key={x.id}>{x.name}</option>)}</Select></Field>
        <Field id="incident-start" label="Outage start (local time)"><Input id="incident-start" name="startedAt" type="datetime-local" required disabled={!!edit}
          defaultValue={edit?.details.startedAt ? localTime(edit.details.startedAt) : ''} /></Field>
        <Field id="incident-restored" label="Restoration (local time)"><Input id="incident-restored" name="restoredAt" type="datetime-local"
          defaultValue={edit?.details.restoredAt ? localTime(edit.details.restoredAt) : ''} /></Field>
        <Field id="incident-state" label="Recovery state"><Select id="incident-state" name="state" defaultValue={edit?.details.state}>{['Investigating', 'Recovering', 'Closed'].map(value =>
          <option key={value}>{value}</option>)}</Select></Field>
      </div>
      <fieldset><legend>Affected components</legend>
        {['Reporting', 'DocumentDelivery', 'Accounting', 'Storage'].map(component => <label className="connect-check" key={component}>
          <input name="components" type="checkbox" value={component} defaultChecked={edit?.details.components?.includes(component) ?? component === 'Reporting'} />{component}
        </label>)}
      </fieldset>
      <Field id="incident-operations" label="Affected reporting operation IDs"><Input id="incident-operations" name="reportingOperationIds" defaultValue={edit?.details.reportingOperationIds?.join(', ')} /></Field>
      <Field id="incident-notes" label="Investigation and recovery notes"><Input id="incident-notes" name="notes" required maxLength={2000} defaultValue={edit?.details.notes} /></Field>
      <Field id="incident-evidence" label="Supporting evidence reference"><Input id="incident-evidence" name="evidenceReference" maxLength={500} defaultValue={edit?.details.evidenceReference} /></Field>
      <div className="connect-actions"><Button type="submit" disabled={busy}>Save incident</Button>{edit &&
        <Button variant="light" onClick={() => setEdit(undefined)}>Cancel</Button>}</div>
    </form>}
    {incidents.length ? <ScrollTable name="Incident history"><thead><tr><th>Outage start</th><th>Recovery</th><th>Revision</th><th>Notes</th><th>Action</th></tr></thead>
      <tbody>{incidents.map(row => <tr key={row.key + row.version}><td>{row.details.startedAt ? manilaTimestamp.format(new Date(row.details.startedAt)) : '—'}</td>
        <td>{row.details.state}</td><td>{row.version}</td><td>{row.details.notes}</td><td>
          {register.data.canManage && current.includes(row) && row.details.state !== 'Closed' && <Button variant="light" onClick={() => setEdit(row)}>Record revision</Button>}</td></tr>)}</tbody>
    </ScrollTable> : <p>No incidents recorded.</p>}
    {register.data.canManage && current.length > 0 && <form className="connect-stack" onSubmit={event => void save(event, 'manual-invoices')}>
      <h3>Manual invoice reference</h3>
      <div className="connect-grid">
        <Field id="manual-incident" label="Outage reference"><Select id="manual-incident" name="incidentId" required>{current.map(x =>
          <option key={x.key} value={x.details.id}>{x.details.startedAt} · {x.details.state}</option>)}</Select></Field>
        <Field id="manual-branch" label="Invoice branch"><Select id="manual-branch" name="branchId" required>{branches.map(x => <option value={x.id} key={x.id}>{x.name}</option>)}</Select></Field>
        {([['series', 'Series'], ['number', 'Original invoice number'], ['date', 'Original transaction date'],
          ['originalEvidenceSha256', 'Original evidence SHA-256'], ['existingDocumentId', 'Existing electronic document ID'],
          ['reviewNotes', 'Review notes']] as const).map(([key, label]) => <Field key={key} id={'manual-' + key} label={label}>
            <Input id={'manual-' + key} name={key} type={key === 'date' ? 'date' : 'text'} required={key !== 'existingDocumentId'} />
          </Field>)}
      </div>
      <div><Button type="submit" disabled={busy}>Record manual reference</Button></div>
    </form>}
    {entries.filter(x => x.kind === 'ManualInvoice').map(x => <p key={x.key}>
      {x.details.original?.series} {x.details.original?.number} · {x.details.original?.date} · Requires verified review
    </p>)}
  </Card>
}
