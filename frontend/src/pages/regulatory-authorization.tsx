import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button, Card, ErrorState, Field, Input, LoadingState, Select } from '@/components/ui'
import { api, errorMessage, type Branch } from '@/lib/account-api'

type Version = {
  number: number; reference: string; authority: string; solution: string; solutionVersion: string; scope: string
  effectiveFrom: string; expiresOn: string | null; filename: string; sha256: string
}
type Authorization = {
  id: string; branchId: string; kind: string; documentType: string; latest: Version; decision: string | null
}
type Register = {
  records: Authorization[]; total: number; kinds: string[]; documentTypes: string[]
  canSubmit: boolean; canViewHistory: boolean; reviewActions: string[]
}
type History = { versions: Version[]; reviews: { number: number; sequence: number; decision: string; reason: string; reviewedAt: string }[] }
type Eligibility = {
  evaluatedOn: string; policyVersion: string; issuance: string; preparation: string; transmission: string
  applicableAuthorizations: { id: string; kind: string; reference: string }[]
  expiredAuthorizations: { id: string; kind: string }[]
}
const labels: Record<string, string> = {
  PermitToIssue: 'Permit to Issue Electronic Invoice', EisCertification: 'EIS Certification',
  PermitToTransmit: 'Permit to Transmit', CasRegistration: 'CAS Registration', PermitToUse: 'Permit to Use',
  AcknowledgmentCertificate: 'Acknowledgment Certificate', SalesInvoice: 'Sales Invoice', CreditMemo: 'Credit Memo',
  ReviewAccepted: 'Accept evidence for internal review', AdditionalEvidence: 'Request additional evidence',
  Rejected: 'Reject evidence', ExternalAuthorizationDocumented: 'Record documented external authorization',
  Suspended: 'Record suspension', Revoked: 'Record revocation',
}

const nextActions: Record<string, string[]> = {
  Pending: ['ReviewAccepted', 'AdditionalEvidence', 'Rejected'],
  ReviewAccepted: ['ExternalAuthorizationDocumented', 'AdditionalEvidence', 'Rejected'],
  ExternalAuthorizationDocumented: ['Suspended', 'Revoked'],
  Suspended: ['Revoked'],
}

export function RegulatoryAuthorizationWorkspace({ base, branches }: { base: string; branches: Branch[] }) {
  const [page, setPage] = useState(1)
  const [editing, setEditing] = useState<Authorization | 'new'>()
  const [selected, setSelected] = useState<Authorization>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const register = useQuery({ queryKey: [base, page], queryFn: () => api<Register>(base + '/?page=' + page) })
  const history = useQuery({
    queryKey: [base, selected?.id, 'history'], enabled: !!selected,
    queryFn: () => api<History>(base + '/' + selected!.id + '/history'),
  })
  const eligibility = useQuery({
    queryKey: [base, selected?.branchId, selected?.documentType, 'eligibility'], enabled: !!selected,
    queryFn: () => api<Eligibility>(base + '/eligibility?' + new URLSearchParams({
      branch: selected!.branchId, documentType: selected!.documentType,
    })),
  })

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!editing || busy) return
    const fields = new FormData(event.currentTarget)
    const previous = editing === 'new' ? undefined : editing
    const attachment = new FormData()
    attachment.set('evidence', fields.get('evidence')!)
    fields.delete('evidence')
    attachment.set('metadata', JSON.stringify({
      ...Object.fromEntries(fields), expiresOn: fields.get('expiresOn') || null,
      branchId: previous?.branchId ?? fields.get('branchId'), kind: previous?.kind ?? fields.get('kind'),
      documentType: previous?.documentType ?? fields.get('documentType'), expectedVersion: previous?.latest.number ?? 0,
    }))
    setBusy(true)
    setError('')
    setMessage('')
    try {
      await api(base + (previous ? '/' + previous.id + '/versions' : '/'), 'POST', attachment)
      setEditing(undefined)
      setSelected(undefined)
      await register.refetch()
      setMessage('Evidence submitted for independent review.')
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }

  async function review(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!selected || !history.data || busy) return
    const form = event.currentTarget
    const fields = new FormData(form)
    const version = history.data.versions[0]
    const prior = history.data.reviews.find(x => x.number === version.number)
    setBusy(true)
    setError('')
    setMessage('')
    try {
      await api(base + '/' + selected.id + '/versions/' + version.number + '/reviews', 'POST', {
        decision: fields.get('decision'), reason: fields.get('reason'), expectedSequence: prior?.sequence ?? 0,
      })
      form.reset()
      await Promise.all([register.refetch(), history.refetch(), eligibility.refetch()])
      setMessage('Review recorded.')
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }

  if (register.isPending) return <LoadingState label="Loading regulatory authorizations…" />
  if (register.isError) return <ErrorState>{errorMessage(register.error)}</ErrorState>
  const result = register.data
  const previous = editing && editing !== 'new' ? editing : undefined
  const latest = history.data?.versions[0]
  const currentDecision = history.data?.reviews.find(x => x.number === latest?.number)?.decision ?? 'Pending'
  const reviewActions = (nextActions[currentDecision] ?? []).filter(action => result.reviewActions.includes(action))

  return <div className="workspace-stack">
    <Card className="workspace-card">
      <h2>Authorization evidence</h2>
      <p>Submit evidence by branch and document type. An independent reviewer records the result. Production issuance and transmission are disabled.</p>
      {error && <ErrorState>{error}</ErrorState>}
      {message && <p role="status" className="workspace-success">{message}</p>}
      {result.canSubmit && <Button disabled={busy} onClick={() => setEditing('new')}>Submit authorization evidence</Button>}
      {!result.records.length && <p>No authorization evidence submitted.</p>}
      {result.records.length > 0 && <div className="table-scroll" role="region" aria-label="Authorization evidence register" tabIndex={0}>
        <table className="workspace-table"><thead><tr><th>Authorization</th><th>Branch</th><th>Reference</th><th>Latest action</th><th>Actions</th></tr></thead>
          <tbody>{result.records.map(row => <tr key={row.id}>
            <td>{labels[row.kind] ?? row.kind}<br />{labels[row.documentType]}</td>
            <td>{branches.find(branch => branch.id === row.branchId)?.code ?? 'Registered branch'}</td>
            <td>{row.latest.reference}</td><td>{row.decision ? labels[row.decision] : 'Awaiting review'}</td>
            <td>{result.canViewHistory && <Button variant="light" disabled={busy} onClick={() => setSelected(row)}>View history</Button>}
              {result.canSubmit && <Button variant="light" disabled={busy} onClick={() => setEditing(row)}>Submit new version</Button>}</td>
          </tr>)}</tbody>
        </table>
      </div>}
      {result.total > 25 && <div className="reporting-pagination">
        <Button variant="light" disabled={page === 1 || busy} onClick={() => setPage(page - 1)}>Previous</Button>
        <span>Page {page}</span>
        <Button variant="light" disabled={page * 25 >= result.total || busy} onClick={() => setPage(page + 1)}>Next</Button>
      </div>}
    </Card>
    {editing && <Card className="workspace-card">
      <h2>{previous ? 'New evidence version' : 'New authorization evidence'}</h2>
      <form key={previous?.id ?? 'new'} className="workspace-form" onSubmit={save}>
        {!previous && <>
          <Field id="reg-branch" label="Covered branch"><Select id="reg-branch" name="branchId" required>
            <option value="">Choose a branch</option>
            {branches.map(x => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}
          </Select></Field>
          <Field id="reg-kind" label="Authorization type"><Select id="reg-kind" name="kind" required>
            {result.kinds.map(x => <option key={x} value={x}>{labels[x] ?? x}</option>)}
          </Select></Field>
          <Field id="reg-doc" label="Covered document type"><Select id="reg-doc" name="documentType" required>
            {result.documentTypes.map(x => <option key={x} value={x}>{labels[x]}</option>)}
          </Select></Field>
        </>}
        <Field id="reg-reference" label="Documented reference"><Input id="reg-reference" name="reference" defaultValue={previous?.latest.reference} maxLength={120} required /></Field>
        <Field id="reg-authority" label="Issuing authority"><Input id="reg-authority" name="authority" defaultValue={previous?.latest.authority} maxLength={120} required /></Field>
        <Field id="reg-solution" label="Solution"><Input id="reg-solution" name="solution" defaultValue={previous?.latest.solution} maxLength={120} required /></Field>
        <Field id="reg-version" label="Solution version"><Input id="reg-version" name="solutionVersion" defaultValue={previous?.latest.solutionVersion} maxLength={80} required /></Field>
        <Field id="reg-scope" label="Documented scope"><Input id="reg-scope" name="scope" defaultValue={previous?.latest.scope} maxLength={1000} required /></Field>
        <Field id="reg-effective" label="Effective from"><Input id="reg-effective" name="effectiveFrom" type="date" defaultValue={previous?.latest.effectiveFrom} required /></Field>
        <Field id="reg-expiry" label="Expires on"><Input id="reg-expiry" name="expiresOn" type="date" defaultValue={previous?.latest.expiresOn ?? undefined} /></Field>
        <Field id="reg-file" label="Evidence file (PDF, PNG, JPEG, or JSON, up to 2 MiB)">
          <Input id="reg-file" name="evidence" type="file" accept=".pdf,.png,.jpg,.jpeg,.json" required />
        </Field>
        {error && <ErrorState>{error}</ErrorState>}
        <div className="reporting-pagination"><Button type="submit" disabled={busy}>Send for review</Button>
          <Button type="button" variant="light" disabled={busy} onClick={() => setEditing(undefined)}>Cancel</Button></div>
      </form>
    </Card>}
    {selected && <Card className="workspace-card" aria-label="Authorization history">
      <h2>Authorization history</h2>
      <Button variant="light" disabled={busy} onClick={() => setSelected(undefined)}>Close history</Button>
      {history.isPending && <LoadingState label="Loading evidence history…" />}
      {history.isError && <ErrorState>{errorMessage(history.error)}</ErrorState>}
      {eligibility.data && <p>As of {eligibility.data.evaluatedOn} in Manila, this branch has {eligibility.data.applicableAuthorizations.length} current documented authorization records and {eligibility.data.expiredAuthorizations.length} expired records for {labels[selected.documentType]}.</p>}
      {history.data?.versions.map(version => <section key={version.number} className="reporting-evidence">
        <h3>Version {version.number} · {version.reference}</h3>
        <p>{version.authority} · {version.solution} {version.solutionVersion}</p>
        <p>{version.scope}</p>
        <p>Effective {version.effectiveFrom}{version.expiresOn ? ' through ' + version.expiresOn : ''}</p>
        <a href={'/api' + base + '/' + selected.id + '/versions/' + version.number + '/evidence'} download>Download {version.filename}</a>
        <p>SHA-256 {version.sha256}</p>
        {history.data!.reviews.filter(event => event.number === version.number).map(event => <p key={event.sequence}>
          {labels[event.decision]} · {new Date(event.reviewedAt).toLocaleString()} · {event.reason}
        </p>)}
      </section>)}
      {reviewActions.length > 0 && <form className="workspace-form" onSubmit={review}>
        <Field id="reg-review-action" label="Review action"><Select id="reg-review-action" name="decision" required>
          {reviewActions.map(action => <option key={action} value={action}>{labels[action]}</option>)}
        </Select></Field>
        <Field id="reg-review-reason" label="Review reason"><Input id="reg-review-reason" name="reason" maxLength={1000} required /></Field>
        {error && <ErrorState>{error}</ErrorState>}
        <Button type="submit" disabled={busy}>Record review</Button>
      </form>}
    </Card>}
  </div>
}
