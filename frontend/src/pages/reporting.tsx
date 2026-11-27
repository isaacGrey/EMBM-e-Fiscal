import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Badge, Button, Card, ErrorState, LoadingState } from '@/components/ui'
import { api, errorMessage } from '@/lib/account-api'

type Register = { mode: string; canPrepare: boolean; canSimulate: boolean; total: number; items: {
  id: string; type: string; number: string; date: string; developmentFixture: boolean; prepared: boolean; simulated: boolean
}[] }
type Detail = { sha256: string; schemaVersion: string; content: unknown; governmentStatus: string }

export function ReportingWorkspace({ base }: { base: string }) {
  const [page, setPage] = useState(1)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [detail, setDetail] = useState<Detail | null>(null)
  const query = useQuery({ queryKey: ['reporting', base, page], queryFn: () => api<Register>(base + '/?page=' + page) })
  async function act(id: string, action: string) {
    setBusy(true); setError(''); setDetail(null)
    try {
      if (action !== 'view') await api(base + '/' + id + '/' + action, 'POST', {})
      setDetail(await api<Detail>(base + '/' + id))
      await query.refetch()
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  if (query.isPending) return <LoadingState label="Loading reporting preparation…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  return <Card className="workspace-card">
    <Badge tone="warning">Preparation mode</Badge>
    <h2>BIR Reporting Preparation</h2>
    <p>Prepare internal evidence from committed Sales Invoices and Credit Memos. Sales Orders are excluded.</p>
    <p>The upcoming BIR transmission API is unavailable. These internal records are not an official BIR payload or submission. Live government transmission and production issuance remain disabled.</p>
    <p>Preparation and local simulation use zero Save &amp; Send Credits. Simulation confirms local integrity only and never records government acceptance.</p>
    {error && <ErrorState>{error}</ErrorState>}
    {!query.data.items.length && <p>No eligible committed documents.</p>}
    <div className="table-scroll"><table className="workspace-table"><thead><tr><th>Document</th><th>Date</th><th>Preparation</th><th>Government status</th><th>Actions</th></tr></thead>
      <tbody>{query.data.items.map(row => <tr key={row.id}>
        <td>{row.number}<br />{row.type}</td><td>{row.date}</td>
        <td>{row.simulated ? 'Local simulation validated' : row.prepared ? 'Prepared' : 'Not prepared'}</td><td>Not submitted</td>
        <td>{!row.prepared && query.data.canPrepare && <Button disabled={busy} onClick={() => void act(row.id, 'prepare')}>Prepare</Button>}
          {row.prepared && <Button disabled={busy} variant="light" onClick={() => void act(row.id, 'view')}>View evidence</Button>}
          {row.prepared && !row.simulated && row.developmentFixture && query.data.canSimulate &&
            <Button disabled={busy} onClick={() => void act(row.id, 'simulate')}>Run local simulation</Button>}</td>
      </tr>)}</tbody></table></div>
    <div className="reporting-pagination"><Button variant="light" disabled={page === 1 || busy} onClick={() => { setDetail(null); setPage(page - 1) }}>Previous</Button>
      <span>Page {page} · {query.data.total} documents</span><Button variant="light" disabled={page * 25 >= query.data.total || busy}
        onClick={() => { setDetail(null); setPage(page + 1) }}>Next</Button></div>
    {detail && <section className="reporting-evidence" aria-label="Preparation evidence"><h3>Internal preparation evidence</h3><p>{detail.schemaVersion} · Not submitted</p>
      <p>SHA-256: {detail.sha256}</p><details><summary>View internal evidence JSON</summary><pre>{JSON.stringify(detail.content, null, 2)}</pre></details></section>}
  </Card>
}
