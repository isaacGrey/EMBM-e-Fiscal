import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { Badge, Button, Card, EmptyState, ErrorState, Field, Input, LoadingState, Select } from '@/components/ui'
import { api, errorMessage, manilaTimestamp } from '@/lib/account-api'
import { ScrollTable } from './connect'
import { useConnectAction } from './field-mapping'

const root = '/vendor/connectors'
const statuses: Record<string, string> = {
  AwaitingEvidence: 'Awaiting evidence', EvidenceRecorded: 'Awaiting independent review',
  EvidenceReviewed: 'Evidence reviewed', ChangesRequested: 'Changes requested', Withdrawn: 'Evidence withdrawn',
  EvidenceStale: 'Evidence needs revalidation', AuthorizationExpired: 'Authorization expired',
}
type Candidate = { id: string; connectorId: string; code: string; number: number; revision: number; name: string }
type Evidence = { edition: string | null; market: string | null; documentationUrl: string | null;
  partnerAccessReference: string | null; authorizationReference: string | null; authorizationThrough: string | null;
  sandboxReportReference: string | null; capabilities: string[] }
type Detail = Candidate & {
  status: string; sequence: number; evidence: Evidence | null; documentation: string[]; capabilities: string[]; missing: string[];
  canRecord: boolean; canReview: boolean; canWithdraw: boolean; productionAccessEnabled: boolean;
  history: { sequence: number; connectorRevision: number; action: string; at: string; reason: string | null; evidence: Evidence | null }[];
}

export function VendorProviderReadiness() {
  const candidates = useQuery({ queryKey: ['vendor-provider-candidates'],
    queryFn: () => api<{ items: Candidate[] }>(root + '/production-readiness'), retry: false })
  const [id, setId] = useState('')
  if (candidates.isPending) return <LoadingState label="Loading provider candidates" />
  if (candidates.isError) return <ErrorState>{errorMessage(candidates.error)}</ErrorState>
  const selected = candidates.data.items.find(item => item.id === id) ?? candidates.data.items[0]
  return <div className="connect-stack">
    <Card className="workspace-card connect-stack"><h2>Production integration readiness</h2>
      <p>Review provider evidence for a specific connector version. Live provider access requires separate activation.</p>
      {selected ? <Field id="provider-candidate" label="Provider version">
        <Select id="provider-candidate" value={selected.id} onChange={event => setId(event.target.value)}>
          {candidates.data.items.map(item => <option key={item.id} value={item.id}>{item.name} · Version {item.number}</option>)}
        </Select>
      </Field> : <EmptyState title="No candidates">Create a review-only connector in the Builder.</EmptyState>}
    </Card>
    {selected && <ProviderPacket key={selected.id} candidate={selected} />}
  </div>
}

function ProviderPacket({ candidate }: { candidate: Candidate }) {
  const endpoint = root + '/' + candidate.connectorId + '/versions/' + candidate.id + '/production-readiness'
  const query = useQuery({ queryKey: ['vendor-provider-packet', candidate.id], queryFn: () => api<Detail>(endpoint), retry: false })
  const action = useConnectAction()
  if (query.isPending) return <LoadingState label="Loading provider evidence" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  const packet = query.data

  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const evidence = Object.fromEntries(form)
    void action.run(async () => {
      await api(endpoint + '/evidence', 'POST', { expectedSequence: packet.sequence, expectedRevision: packet.revision,
        evidence: { ...evidence, authorizationThrough: evidence.authorizationThrough || null, capabilities: form.getAll('capabilities') } })
      await query.refetch()
    }, 'Evidence recorded.')
  }

  function review(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    void action.run(async () => {
      await api(endpoint + '/review', 'POST', { expectedSequence: packet.sequence, expectedRevision: packet.revision,
        decision: form.get('decision'), reason: form.get('reason') })
      await query.refetch()
    }, 'Review recorded.')
  }

  return <div className="connect-stack">
    <Card className="workspace-card connect-stack">
      <h3>{packet.name}</h3>
      <div className="connect-actions">
        <Badge>{statuses[packet.status]}</Badge>
        <Badge tone="warning">Production access disabled</Badge>
      </div>
      <Link className="text-link" to={'/portal/vendor/connectors/' + packet.connectorId + '?version=' + packet.id}>Open connector configuration</Link>
      {packet.missing.length > 0 && <div><h4>Required evidence</h4><ul>{packet.missing.map(item => <li key={item}>{item}</li>)}</ul></div>}
      {packet.evidence && <dl className="workspace-definitions">
        {([['Edition', packet.evidence.edition], ['Market', packet.evidence.market],
          ['Documentation', packet.evidence.documentationUrl], ['Partner access', packet.evidence.partnerAccessReference],
          ['Authorization', packet.evidence.authorizationReference], ['Authorized through', packet.evidence.authorizationThrough],
          ['Sandbox report', packet.evidence.sandboxReportReference], ['Capabilities', packet.evidence.capabilities.join(', ')]] as const)
          .map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value || '—'}</dd></div>)}
      </dl>}
      {action.error && <ErrorState>{action.error}</ErrorState>}
      {action.notice && <p role="status">{action.notice}</p>}
    </Card>
    {packet.canRecord && <Card className="workspace-card connect-stack"><h3>Record provider evidence</h3>
      <p>Enter document reference IDs. Keep credentials and customer records in their authorized systems.</p>
      <form key={packet.sequence + ':' + packet.revision} onSubmit={save} className="connect-stack">
        <fieldset className="connect-grid connect-fieldset" disabled={action.busy}>
          {([['edition', 'Provider edition', 120], ['market', 'Two-letter market code', 2],
            ['partnerAccessReference', 'Partner access reference', 160], ['authorizationReference', 'Authorization reference', 160],
            ['sandboxReportReference', 'Authorized sandbox report reference', 160]] as const).map(([field, label, maxLength]) =>
            <Field key={field} id={'provider-' + field} label={label}><Input id={'provider-' + field} name={field}
              defaultValue={packet.evidence?.[field] ?? ''} maxLength={maxLength} /></Field>)}
          <Field id="provider-authorization-through" label="Authorization valid through">
            <Input id="provider-authorization-through" name="authorizationThrough" type="date" defaultValue={packet.evidence?.authorizationThrough ?? ''} />
          </Field>
          <Field id="provider-documentation" label="Registered documentation">
            <Select id="provider-documentation" name="documentationUrl" defaultValue={packet.evidence?.documentationUrl ?? ''}>
              <option value="">Select documentation</option>
              {packet.documentation.map(url => <option key={url} value={url}>{url}</option>)}
            </Select>
          </Field>
        </fieldset>
        {packet.capabilities.length > 0 && <fieldset className="connect-fieldset" disabled={action.busy}>
          <legend>Capabilities covered by the evidence</legend>
          {packet.capabilities.map(code => <label className="connect-check" key={code}>
            <input type="checkbox" name="capabilities" value={code} defaultChecked={packet.evidence?.capabilities.includes(code)} />{code}
          </label>)}
        </fieldset>}
        <div><Button type="submit" disabled={action.busy}>Save evidence</Button></div>
      </form>
    </Card>}
    {(packet.canReview || packet.canWithdraw) && <Card className="workspace-card connect-stack"><h3>Review evidence</h3>
      <form key={packet.sequence} className="connect-actions" onSubmit={review}>
        <Field id="provider-review-decision" label="Decision"><Select id="provider-review-decision" name="decision">
          {packet.canReview && <><option value="ChangesRequested">Request changes</option>
            {packet.missing.length === 0 && <option value="EvidenceReviewed">Confirm evidence review</option>}</>}
          {packet.canWithdraw && <option value="Withdrawn">Withdraw evidence</option>}
        </Select></Field>
        <Field id="provider-review-reason" label="Review reason"><Input id="provider-review-reason" name="reason" maxLength={500} required /></Field>
        <Button type="submit" disabled={action.busy}>Record decision</Button>
      </form>
    </Card>}
    {packet.history.length > 0 && <Card className="workspace-card connect-stack"><h3>Evidence history</h3>
      <ScrollTable name="Provider evidence history"><thead><tr><th>Event</th><th>Connector revision</th><th>Time</th><th>Details</th></tr></thead>
        <tbody>{packet.history.map(entry => <tr key={entry.sequence}>
          <td>{statuses[entry.action]}</td><td>{entry.connectorRevision}</td>
          <td>{manilaTimestamp.format(new Date(entry.at))}</td>
          <td>{entry.evidence ? <details><summary>Recorded references</summary>
            <pre className="connect-schema">{JSON.stringify(entry.evidence, null, 2)}</pre>
          </details> : entry.reason}</td>
        </tr>)}</tbody>
      </ScrollTable>
    </Card>}
  </div>
}
