import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button, Card, ErrorState, Field, Input, LoadingState, Select } from '@/components/ui'
import { api, errorMessage } from '@/lib/account-api'

type Category = 'Application' | 'Checklist' | 'Requirement' | 'MaterialChange'
type Version = { key: string; category: Category; number: number; details: Record<string, string | number | null>
  recordedAt: string; filename: string | null; sha256: string | null }
type Register = { provider: string; solution: string; presentationReport: string; canEdit: boolean
  total: number; checklist: { key: string; title: string }[]; items: Version[] }
type FormField = { key: string; label: string; type?: string; options?: string[]; required?: boolean; max?: number }

const base = '/vendor/accreditation'
const tabs: Record<Category, string> = {
  Application: 'ESP Accreditation', Checklist: 'Accreditation Readiness',
  Requirement: 'Regulatory Requirements', MaterialChange: 'Material Change Reviews',
}
const readiness = ['UnderReview', 'InProgress', 'EvidenceReady', 'AwaitingExternalValidation']
const source: FormField = { key: 'sourceUrl', label: 'Official BIR source URL', type: 'url', max: 500 }
const fields: Record<Category, FormField[]> = {
  Application: [
    { key: 'status', label: 'Application status', options: ['Preparing', 'Submitted', 'UnderEvaluation', 'Closed'] },
    { key: 'reference', label: 'Application reference' }, { key: 'submittedOn', label: 'Submission date', type: 'date' },
    { key: 'presentationStatus', label: 'Presentation status', options: ['NotScheduled', 'Requested', 'Scheduled', 'Presented'] },
    { key: 'evaluationStatus', label: 'Technical evaluation', options: ['NotStarted', 'InProgress', 'Complete'] },
    { key: 'externalDecisionStatus', label: 'External decision', options: ['Pending', 'DecisionReceived'] },
    { key: 'certificateReference', label: 'Documented certificate reference' },
    { key: 'solutionVersion', label: 'Documented solution version', max: 80 },
    { key: 'modules', label: 'Documented modules', max: 2000 }, { key: 'infrastructure', label: 'Documented infrastructure', max: 2000 },
  ],
  Checklist: [
    { key: 'status', label: 'Readiness status', options: readiness }, source,
    { key: 'reference', label: 'Source reference' }, { key: 'implementationStatus', label: 'Implementation status', max: 80 },
  ],
  Requirement: [
    { key: 'status', label: 'Source review status', options: ['UnderReview', 'AwaitingExternalValidation', 'SourceReviewed'] },
    { key: 'reference', label: 'Issuance reference', required: true }, { ...source, required: true },
    { key: 'section', label: 'Regulatory section', required: true, max: 160 },
    { key: 'description', label: 'Requirement description', required: true, max: 4000 },
    { key: 'applicability', label: 'Applicability', required: true, max: 2000 },
    { key: 'effectiveDate', label: 'Effective date', type: 'date' }, { key: 'sourceReviewedOn', label: 'Source review date', type: 'date' },
    { key: 'implementationStatus', label: 'Implementation status', max: 80 },
    { key: 'supersedes', label: 'Superseded requirement key', max: 64 },
  ],
  MaterialChange: [
    { key: 'status', label: 'Review status', options: readiness },
    { key: 'solutionVersion', label: 'Affected solution version', required: true, max: 80 },
    { key: 'description', label: 'Change description', max: 4000 },
    { key: 'applicability', label: 'Affected systems and scope', max: 2000 }, source,
  ],
}
const human = (value: string) => value.replace(/([a-z])([A-Z])/g, '$1 $2')

export function VendorAccreditationWorkspace() {
  const [category, setCategory] = useState<Category>('Application')
  const [page, setPage] = useState(1)
  const [edit, setEdit] = useState<{ key?: string; title?: string; current?: Version }>()
  const [selected, setSelected] = useState<string>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const register = useQuery({ queryKey: [base, category, page], queryFn: () => api<Register>(base + '?category=' + category + '&page=' + page) })
  const history = useQuery({
    queryKey: [base, category, selected], enabled: !!selected,
    queryFn: () => api<Version[]>(base + '/' + category + '/' + selected),
  })

  function changeTab(next: Category) {
    setCategory(next); setPage(1); setEdit(undefined); setSelected(undefined); setError(''); setMessage('')
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!edit || busy) return
    const input = new FormData(event.currentTarget)
    const key = edit.key ?? String(input.get('key'))
    const file = input.get('evidence')
    input.delete('key')
    input.delete('evidence')
    const data = new FormData()
    const values = Object.fromEntries([...input].map(([name, value]) => [name, String(value).trim() || null]))
    data.set('metadata', JSON.stringify({ ...values, title: edit.title ?? values.title, expectedVersion: edit.current?.number ?? 0 }))
    if (file instanceof File && file.size > 0) data.set('evidence', file)
    setBusy(true); setError(''); setMessage('')
    try {
      await api(base + '/' + category + '/' + encodeURIComponent(key), 'POST', data)
      setEdit(undefined)
      await register.refetch()
      if (selected === key) await history.refetch()
      setMessage('Version recorded.')
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }

  const formFields: FormField[] = [...fields[category],
    { key: 'notes', label: category === 'MaterialChange' ? 'Regulatory impact assessment' : 'Reviewer notes', max: 4000, required: category === 'MaterialChange' },
    { key: 'outstanding', label: category === 'Requirement' ? 'Interpretation questions' : 'Outstanding evidence or actions', max: 4000 },
  ]
  if (register.isPending) return <LoadingState label="Loading provider accreditation…" />
  if (register.isError) return <ErrorState>{errorMessage(register.error)}</ErrorState>
  const data = register.data
  const rows = category === 'Checklist' ? data.checklist.map(x => ({ ...x, current: data.items.find(item => item.key === x.key) }))
    : category === 'Application' ? [{ key: 'embm-efiscal', title: data.solution, current: data.items[0] }]
    : data.items.map(current => ({ key: current.key, title: String(current.details.title), current }))

  return <div className="workspace-stack">
    <Card className="workspace-card">
      <h2>Regulatory Compliance</h2>
      <p>{data.provider} · {data.solution}</p>
      <p>{data.presentationReport}</p>
      <p>Official accreditation verification is pending.</p>
      <nav className="reporting-pagination" aria-label="Regulatory compliance registers">
        {(Object.keys(tabs) as Category[]).map(tab => <Button key={tab} variant={category === tab ? 'primary' : 'light'}
          disabled={busy} aria-pressed={category === tab} onClick={() => changeTab(tab)}>{tabs[tab]}</Button>)}
      </nav>
      <h3>{tabs[category]}</h3>
      {error && <ErrorState>{error}</ErrorState>}
      {message && <p role="status">{message}</p>}
      {data.canEdit && ['Requirement', 'MaterialChange'].includes(category) && <Button onClick={() => setEdit({})}>Add record</Button>}
      <ul className="workspace-stack">{rows.map(row => <li key={row.key} className="reporting-pagination">
        <strong>{row.title}</strong><span>{row.current ? human(String(row.current.details.status)) : 'Awaiting evidence'}</span>
        {row.current && <Button variant="light" disabled={busy} onClick={() => setSelected(row.key)}>View history</Button>}
        {data.canEdit && <Button variant="light" disabled={busy} onClick={() => setEdit({
          key: row.key, title: category === 'Application' || category === 'Checklist' ? row.title : undefined, current: row.current,
        })}>Record version</Button>}
      </li>)}</ul>
      {data.total > 25 && <div className="reporting-pagination">
        <Button variant="light" disabled={page === 1 || busy} onClick={() => setPage(page - 1)}>Previous</Button>
        <span>Page {page}</span>
        <Button variant="light" disabled={page * 25 >= data.total || busy} onClick={() => setPage(page + 1)}>Next</Button>
      </div>}
    </Card>
    {edit && <Card className="workspace-card">
      <h3>{edit.title ?? 'Record details'}</h3>
      <form className="workspace-form" key={edit.key ?? 'new'} onSubmit={save}>
        {!edit.key && <Field id="ac-key" label="Record key"><Input id="ac-key" name="key" pattern="[a-z0-9][a-z0-9\-]{0,63}" required /></Field>}
        {!edit.title && <Field id="ac-title" label="Title"><Input id="ac-title" name="title" maxLength={160} required defaultValue={String(edit.current?.details.title ?? '')} /></Field>}
        {formFields.map(field => {
          const id = 'ac-' + field.key
          const value = edit.current?.details[field.key]
          return <Field key={field.key} id={id} label={field.label}>
            {field.options ? <Select id={id} name={field.key} defaultValue={String(value ?? field.options[0])} required>
              {field.options.map(option => <option key={option} value={option}>{human(option)}</option>)}
            </Select> : <Input id={id} name={field.key} type={field.type ?? 'text'} defaultValue={String(value ?? '')}
              required={field.required} maxLength={field.max ?? 120} readOnly={field.key === 'supersedes' && !!edit.current} />}
          </Field>
        })}
        <Field id="ac-evidence" label="Supporting evidence (optional, up to 2 MiB)">
          <Input id="ac-evidence" name="evidence" type="file" accept=".pdf,.png,.jpg,.jpeg,.json" />
        </Field>
        {error && <ErrorState>{error}</ErrorState>}
        <div className="reporting-pagination"><Button type="submit" disabled={busy}>Save version</Button>
          <Button type="button" variant="light" disabled={busy} onClick={() => setEdit(undefined)}>Cancel</Button></div>
      </form>
    </Card>}
    {selected && <Card className="workspace-card">
      <h3>Version history</h3>
      {history.isPending && <LoadingState label="Loading version history…" />}
      {history.isError && <ErrorState>{errorMessage(history.error)}</ErrorState>}
      {history.data?.map(row => <section className="reporting-evidence" key={row.number}>
        <h4>Version {row.number} · {row.details.title}</h4>
        <p>{new Date(row.recordedAt).toLocaleString()}</p>
        <dl className="workspace-definitions">{formFields.filter(field => row.details[field.key]).map(field =>
          <div key={field.key}><dt>{field.label}</dt><dd>{field.options ? human(String(row.details[field.key])) : row.details[field.key]}</dd></div>)}</dl>
        {row.filename && <><a href={'/api' + base + '/' + category + '/' + selected + '/' + row.number + '/evidence'} download>Download {row.filename}</a>
          <p>SHA-256 {row.sha256}</p></>}
      </section>)}
      <Button variant="light" onClick={() => setSelected(undefined)}>Close history</Button>
    </Card>}
  </div>
}
