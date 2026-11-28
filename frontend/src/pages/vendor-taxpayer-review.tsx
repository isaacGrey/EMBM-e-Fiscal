import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button, Card, ErrorState, Field, Input, LoadingState, Select } from '@/components/ui'
import { api, errorMessage } from '@/lib/account-api'

const base = '/vendor/taxpayer-review'
type Case = { id: string; branchId: string; kind: string; documentType: string; canReview: boolean }
type Reviewer = { id: string; fullName: string; enrolled: boolean }
type Assignment = { id: string; reviewer: string; reason: string; startsAt: string; expiresAt: string }
type Detail = {
  id: string; canReview: boolean; company?: string; branch?: { name: string; code: string }
  rdo?: { code: string; registrationStatus: string; effectiveFrom: string }[]
  assignments: { assignment: Assignment; revocation?: { reason: string } }[]
  versions?: { number: number; reference: string; authority: string; scope: string; filename: string; sha256: string; scanState?: string }[]
  reviews?: { number: number; sequence: number; decision: string; reason: string }[]
}

export function VendorTaxpayerReview() {
  const [selected, setSelected] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const queue = useQuery({ queryKey: [base], queryFn: () => api<{ items: Case[]; canAssign: boolean }>(base), retry: false })
  const staff = useQuery({ queryKey: [base, 'reviewers'], queryFn: () => api<Reviewer[]>(base + '/reviewers'), enabled: !!queue.data?.canAssign })
  const detail = useQuery({ queryKey: [base, selected], queryFn: () => api<Detail>(base + '/' + selected), enabled: !!selected, retry: false })
  async function run(command: () => Promise<unknown>) {
    setBusy(true); setError('')
    try {
      await command()
      await Promise.all([queue.refetch(), selected ? detail.refetch() : undefined, queue.data?.canAssign ? staff.refetch() : undefined])
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  function assign(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const expiresAt = new Date(String(form.get('expiresAt')) + '+08:00')
    if (!Number.isFinite(expiresAt.getTime())) return
    const start = form.get('startsAt')
    const startsAt = start ? new Date(String(start) + '+08:00') : new Date()
    void run(() => api(base + '/' + selected + '/assignments', 'POST', {
      reviewer: form.get('reviewer'), reason: form.get('reason'), startsAt: startsAt.toISOString(), expiresAt: expiresAt.toISOString(),
    }))
  }
  const row = detail.data
  const latest = row?.versions?.[0]
  const prior = row?.reviews?.find(x => x.number === latest?.number)
  return <Card className="workspace-card workspace-stack">
    <h2>Taxpayer verification</h2>
    <p>Development review of company-submitted evidence. Review decisions record internal findings. Official authorization and runtime eligibility are tracked separately.</p>
    {error && <ErrorState>{error}</ErrorState>}
    {queue.isPending ? <LoadingState /> : queue.isError ? <ErrorState>{errorMessage(queue.error)}</ErrorState> :
      !queue.data.items.length ? <p>No review cases are available.</p> : <div className="table-scroll" role="region" aria-label="Taxpayer review queue" tabIndex={0}>
        <table className="workspace-table"><thead><tr><th>Case</th><th>Authorization</th><th>Document</th></tr></thead>
          <tbody>{queue.data.items.map(c => <tr key={c.id}><td><Button variant="light" onClick={() => setSelected(c.id)}>{c.id.slice(0, 8)}</Button></td><td>{c.kind}</td><td>{c.documentType}</td></tr>)}</tbody>
        </table>
      </div>}
    {selected && detail.isPending && <LoadingState />}
    {selected && detail.isError && <ErrorState>{errorMessage(detail.error)}</ErrorState>}
    {row && <>
      <h3>Case {row.id.slice(0, 8)}</h3>
      {queue.data?.canAssign && <>
        <details><summary>Enroll a vendor reviewer</summary>
          {(staff.data ?? []).filter(r => !r.enrolled).map(r => <Button key={r.id} variant="light" disabled={busy} onClick={() =>
            void run(() => api(base + '/reviewers/' + r.id, 'PUT', {}))}>Enroll {r.fullName}</Button>)}
        </details>
        <form className="workspace-form" onSubmit={assign}>
          <Field id="vendor-reviewer" label="Assigned reviewer"><Select id="vendor-reviewer" name="reviewer" required><option value="">Choose reviewer</option>
            {(staff.data ?? []).filter(r => r.enrolled).map(r => <option key={r.id} value={r.id}>{r.fullName} · {r.id.slice(0, 8)}</option>)}
          </Select></Field>
          <Field id="vendor-start" label="Starts at Manila time (optional)"><Input id="vendor-start" name="startsAt" type="datetime-local" /></Field>
          <Field id="vendor-expiry" label="Expires at Manila time"><Input id="vendor-expiry" name="expiresAt" type="datetime-local" required /></Field>
          <Field id="vendor-reason" label="Assignment reason"><Input id="vendor-reason" name="reason" required maxLength={600} /></Field>
          <Button type="submit" disabled={busy}>Assign case</Button>
        </form>
        {row.assignments.map(({ assignment: a, revocation }) => <section key={a.id}>
          <p>{staff.data?.find(r => r.id === a.reviewer)?.fullName ?? 'Reviewer'} · {a.reason} · {a.startsAt} to {a.expiresAt}</p>
          {revocation ? <p>Revoked · {revocation.reason}</p> : <form className="connect-actions" onSubmit={e => {
            e.preventDefault()
            const reason = new FormData(e.currentTarget).get('reason')
            void run(() => api(base + '/' + selected + '/assignments/' + a.id + '/revoke', 'POST', { reason }))
          }}>
            <Field id={'revoke-' + a.id} label="Revocation reason"><Input id={'revoke-' + a.id} name="reason" required maxLength={600} /></Field>
            <Button type="submit" variant="light" disabled={busy}>Revoke assignment</Button>
          </form>}
        </section>)}
      </>}
      {row.canReview && latest && <>
        <p>{row.company} · {row.branch?.code} · {row.branch?.name}</p>
        {row.rdo?.map((rdo, i) => <p key={i}>RDO {rdo.code} · {rdo.registrationStatus} · {rdo.effectiveFrom}</p>)}
        {row.versions?.map(v => <section key={v.number}><h4>Evidence v{v.number} · {v.reference}</h4>
          <p>{v.authority} · {v.scope}</p><p>{v.scanState ?? 'Legacy evidence'} · SHA-256 {v.sha256}</p>
          {(v.scanState == null || v.scanState === 'DevelopmentClean') && <a className="text-link" href={'/api' + base + '/' + selected + '/versions/' + v.number + '/evidence'} download>{v.filename}</a>}
        </section>)}
        {row.reviews?.map(r => <p key={r.number + ':' + r.sequence}>v{r.number} · {r.decision} · {r.reason}</p>)}
        {(!prior || prior.decision === 'ReviewAccepted') && <form className="workspace-form" onSubmit={e => {
          e.preventDefault()
          const form = new FormData(e.currentTarget)
          void run(() => api(base + '/' + selected + '/versions/' + latest.number + '/reviews', 'POST',
            { decision: form.get('decision'), reason: form.get('reason'), expectedSequence: prior?.sequence ?? 0 }))
        }}>
          <Field id="vendor-decision" label="Review decision"><Select id="vendor-decision" name="decision">
            {!prior && <option value="ReviewAccepted">Accept internal review</option>}
            <option value="AdditionalEvidence">Request additional evidence</option><option value="Rejected">Reject evidence</option>
          </Select></Field>
          <Field id="vendor-note" label="Review note"><Input id="vendor-note" name="reason" required maxLength={1000} /></Field>
          <Button type="submit" disabled={busy}>Record review</Button>
        </form>}
      </>}
    </>}
  </Card>
}
