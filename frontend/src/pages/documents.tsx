import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { Badge, Button, Card, ErrorState, Field, Input, LoadingState, Select, Table } from '@/components/ui'
import { api, errorMessage, manilaDate, manilaTimestamp } from '@/lib/account-api'
import {
  documentLabels, documentsRoot, documentTypes, statusName, type Choice, type DeliveryAttempt, type DocumentOptions,
  type DocumentReceipt, type DocumentRegisterData, type DocumentType,
} from '@/lib/document-api'
import { Reference } from './master-fields'
import { DocumentForm } from './document-form'
import { DocumentViewer } from './document-viewer'
import './documents.css'

export const queryRoot = (base: string) => ['fiscal-documents', base]

export function DocumentsWorkspace({ base, companyPath, section, branch }: { base: string; companyPath: string; section: string; branch?: string }) {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const segments = section.split('/')
  const view = segments[1]
  const selected = segments[2]
  const type = documentTypes.find(item => item === view)
  const options = useQuery({ queryKey: [...queryRoot(base), 'options', type],
    queryFn: () => api<DocumentOptions>(base + '/' + type + '/options'), enabled: !!type, retry: false })
  if (view === 'numbering') return <Numbering base={base} />
  if (view === 'delivery') return <DeliveryHistory base={base} />
  if (!type) return <Card className="workspace-card doc-panel">
    <h2>Company document workstations</h2>
    <p>Sales Invoice and Credit Memo use Save &amp; Send · 1 credit each.</p>
    <p>Sales Order uses Save · 0 credits.</p>
    <div className="doc-actions">{documentTypes.map(kind => <Button key={kind} asChild variant="light"><Link to={documentsRoot + '/' + kind}>
      {documentLabels[kind]} register</Link></Button>)}</div>
  </Card>
  if (options.isPending) return <LoadingState label="Loading document options…" />
  if (options.isError) return <ErrorState>{errorMessage(options.error)}</ErrorState>
  if (selected === 'new') return options.data.canWrite
    ? <DocumentForm key={base + type + (params.get('externalSource') ?? '')} base={base} master={companyPath + '/master-data'} type={type} options={options.data}
      branch={branch} source={params.get('source') ?? ''} externalKey={params.get('externalSource') ?? ''} onCommitted={doc => navigate(documentsRoot + '/' + doc.type + '/' + doc.id, { replace: true })} />
    : <ErrorState>Document creation is unavailable for this role.</ErrorState>
  if (selected) return <DocumentViewer key={selected} base={base} type={type} id={selected} options={options.data} />
  return <Register key={base + type} base={base} master={companyPath + '/master-data'} type={type} options={options.data} />
}

function Register({ base, master, type, options }: { base: string; master: string; type: DocumentType; options: DocumentOptions }) {
  const [filters, setFilters] = useState({ q: '', number: '', customer: '', branch: '', from: '', through: '', source: '', charged: '', delivery: '', accounting: '', regulator: '' })
  const [page, setPage] = useState(1)
  const params = new URLSearchParams(Object.entries(filters).filter(([, value]) => value !== ''))
  params.set('page', String(page))
  const path = base + '/' + type + '/register?' + params
  const query = useQuery({ queryKey: [...queryRoot(base), 'register', type, params.toString()], queryFn: () => api<DocumentRegisterData>(path), retry: false })
  const change = (key: keyof typeof filters, value: string) => { setPage(1); setFilters(previous => ({ ...previous, [key]: value })) }
  const text = (key: keyof typeof filters, title: string, type = 'text') => <Field id={'register-' + key} label={title}>
    <Input id={'register-' + key} value={filters[key]} type={type} onChange={e => change(key, e.target.value)} /></Field>
  const select = (key: keyof typeof filters, title: string, choices: string[]) => <Field id={'register-' + key} label={title}>
    <Select id={'register-' + key} value={filters[key]} onChange={e => change(key, e.target.value)}>
      <option value="">All</option>{choices.map(value => <option value={value} key={value}>{statusName(value)}</option>)}
    </Select></Field>
  return <Card className="workspace-card doc-panel">
    <div className="doc-toolbar">
      <div><h2>{documentLabels[type]} register</h2>{query.data && <p className="doc-note">{query.data.total} committed</p>}</div>
      <div className="doc-actions">
        {options.canWrite && <Button asChild><Link to={documentsRoot + '/' + type + '/new'}>New {documentLabels[type]}</Link></Button>}
        {options.canExport && <a className="text-link" href={'/api' + path + '&export=true'} download>Export CSV</a>}
      </div>
    </div>
    <div className="doc-grid">
      {text('q', 'Search by document or customer', 'search')}
      <Field id="register-branch" label="Branch"><Select id="register-branch" value={filters.branch} onChange={e => change('branch', e.target.value)}>
        <option value="">All branches</option>{options.branches.map(x => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}</Select></Field>
      {text('from', 'From date', 'date')}{text('through', 'Through date', 'date')}
    </div>
    <details><summary>More filters</summary><div className="doc-grid">
      {text('number', 'Document number')}
      <Reference id="register-customer" label="Customer" kind="customers" base={master} value={filters.customer} onChange={value => change('customer', value)} />
      {text('source', 'Source reference')}
      {type !== 'SalesOrder' && select('delivery', 'Electronic delivery', ['PendingPdf', 'Queued', 'Sending', 'AcceptedByProvider', 'Failed', 'RetryScheduled', 'RequiresReview', 'Delivered', 'Bounced'])}
      {select('accounting', 'Accounting synchronization', ['AwaitingConnection', 'NotConfigured'])}
      {select('regulator', 'BIR reporting', ['AuthorizationRequired', 'NotApplicable'])}
      <Field id="register-charged" label="Credit consumption"><Select id="register-charged" value={filters.charged} onChange={e => change('charged', e.target.value)}>
        <option value="">All</option><option value="true">One credit</option><option value="false">Zero credits</option>
      </Select></Field>
    </div></details>
    {query.isPending ? <LoadingState /> : query.isError ? <ErrorState>{errorMessage(query.error)}</ErrorState> :
      query.data.items.length ? <div className="doc-table" role="region" aria-label={documentLabels[type] + ' records'} tabIndex={0}>
        <Table><thead><tr>{['Number', 'Date', 'Customer', 'Total', 'Source', 'Credits', 'Delivery', 'Accounting'].map(x => <th scope="col" key={x}>{x}</th>)}</tr></thead>
          <tbody>{query.data.items.map(row => <tr key={row.document.id}><td><DocumentLink doc={row.document} /></td><td>{row.document.date}</td>
            <td>{row.document.customerName}</td><td>{row.document.currency} {row.document.total}</td><td>{row.sourceNumber || '—'}</td>
            <td>{row.document.credits}</td><td>{statusName(row.electronicDelivery)}</td><td>{statusName(row.accounting)}</td></tr>)}</tbody>
        </Table>
      </div> : <p>No committed documents match these filters.</p>}
    {query.data && <Pages page={page} total={query.data.total} size={query.data.pageSize} change={setPage} />}
  </Card>
}

export function DocumentLink({ doc }: { doc: Pick<DocumentReceipt, 'id' | 'type' | 'number'> }) {
  return <Link className="text-link" to={documentsRoot + '/' + doc.type + '/' + doc.id}>{doc.number}</Link>
}

export function Pages({ page, total, size, change }: { page: number; total: number; size: number; change: (page: number) => void }) {
  if (total <= size && page === 1) return null
  return <div className="doc-actions">
    <Button variant="light" disabled={page === 1} onClick={() => change(page - 1)}>Previous</Button>
    <span>Page {page} of {Math.max(1, Math.ceil(total / size))}</span>
    <Button variant="light" disabled={page * size >= total} onClick={() => change(page + 1)}>Next</Button>
  </div>
}

type Series = { id: string; type: DocumentType; branchId: string | null; effectiveFrom: string; prefix: string; digits: number; revision: number; nextNumber: number; initialNumber: number }
type SeriesList = { series: Series[]; branches: Choice[] }
function Numbering({ base }: { base: string }) {
  const client = useQueryClient()
  const [type, setType] = useState<DocumentType>('SalesInvoice')
  const [branch, setBranch] = useState('')
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const query = useQuery({ queryKey: [...queryRoot(base), 'numbering'], queryFn: () => api<SeriesList>(base + '/numbering'), retry: false })
  if (query.isPending) return <LoadingState label="Loading numbering revisions…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  const latest = query.data.series.find(x => x.type === type && (x.branchId ?? '') === branch)
  const save = async (fields: FormData) => {
    setBusy(true); setError(''); setMessage('')
    try {
      await api(base + '/numbering/' + type, 'POST', { branchId: branch || null, effectiveFrom: fields.get('effective'),
        prefix: fields.get('prefix'), digits: Number(fields.get('digits')), initialNumber: Number(fields.get('number')), expectedRevision: latest?.revision ?? 0 })
      await client.invalidateQueries({ queryKey: queryRoot(base) })
      setMessage('Numbering revision published.')
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  return <Card className="workspace-card doc-panel">
    <h2>Document numbering</h2>
    <p className="doc-note">Select a company or branch series. Numbers are assigned during commitment. Local invoice series are for development use.</p>
    <form className="doc-form" onSubmit={e => { e.preventDefault(); void save(new FormData(e.currentTarget)) }}>
      <fieldset className="doc-grid" disabled={busy}>
        <Field id="series-type" label="Document type"><Select id="series-type" value={type} onChange={e => setType(e.target.value as DocumentType)}>
          {documentTypes.map(x => <option key={x} value={x}>{documentLabels[x]}</option>)}</Select></Field>
        <Field id="series-branch" label="Branch scope"><Select id="series-branch" value={branch} onChange={e => setBranch(e.target.value)}>
          <option value="">Company-wide</option>{query.data.branches.map(x => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}</Select></Field>
        <Field id="series-prefix" label="Prefix"><Input key={type + branch + (latest?.revision ?? 0)} id="series-prefix" name="prefix" required maxLength={24}
          defaultValue={latest?.prefix ?? ({ SalesInvoice: 'SI-', SalesOrder: 'SO-', CreditMemo: 'CM-' })[type]} /></Field>
        <Field id="series-length" label="Number length"><Input key={'digits' + type + branch + latest?.revision} id="series-length" name="digits" type="number"
          min="3" max="12" defaultValue={latest?.digits ?? 6} required /></Field>
        <Field id="series-number" label="Starting sequence"><Input key={'sequence' + type + branch + latest?.revision} id="series-number" name="number"
          type="number" min="1" defaultValue={latest?.nextNumber ?? 1} required /></Field>
        <Field id="series-effective" label="Effective from"><Input id="series-effective" name="effective" type="date" defaultValue={manilaDate()} required /></Field>
      </fieldset>
      {error && <ErrorState>{error}</ErrorState>}
      {message && <p role="status">{message}</p>}
      <div><Button type="submit" disabled={busy}>Publish numbering revision</Button></div>
    </form>
    <h3>Numbering history</h3>
    <div className="doc-table" role="region" aria-label="Series revisions" tabIndex={0}><Table>
      <thead><tr>{['Type', 'Scope', 'Prefix', 'Revision', 'Effective', 'Next sequence'].map(x => <th scope="col" key={x}>{x}</th>)}</tr></thead>
      <tbody>{query.data.series.map(row => <tr key={row.id}><td>{documentLabels[row.type]}</td>
        <td>{query.data.branches.find(x => x.id === row.branchId)?.code ?? 'Company-wide'}</td><td>{row.prefix}</td>
        <td>{row.revision}</td><td>{row.effectiveFrom}</td><td>{row.nextNumber}</td></tr>)}</tbody>
    </Table></div>
  </Card>
}

type HistoryPage = { page: number; pageSize: number; total: number; items: { document: Pick<DocumentReceipt, 'id' | 'type' | 'number'>; attempt: DeliveryAttempt }[] }
function DeliveryHistory({ base }: { base: string }) {
  const [page, setPage] = useState(1)
  const query = useQuery({ queryKey: [...queryRoot(base), 'delivery', page],
    queryFn: () => api<HistoryPage>(base + '/delivery-history?page=' + page), refetchInterval: 5000, retry: false })
  return <Card className="workspace-card doc-panel"><h2>Email delivery history</h2>
    {query.isPending ? <LoadingState /> : query.isError ? <ErrorState>{errorMessage(query.error)}</ErrorState> :
      query.data.items.length ? <>
        <div className="doc-table" role="region" aria-label="Delivery attempt history" tabIndex={0}><Table>
          <thead><tr>{['Document', 'Time', 'Recipient', 'Attempt', 'Status', 'Reference'].map(x => <th scope="col" key={x}>{x}</th>)}</tr></thead>
          <tbody>{query.data.items.map(({ document, attempt }) => <tr key={attempt.id}><td><DocumentLink doc={document} /></td>
            <td>{manilaTimestamp.format(new Date(attempt.at))}</td><td>{attempt.recipient}</td><td>{attempt.attempt}</td>
            <td><Badge>{statusName(attempt.status)}</Badge></td><td>{attempt.providerReference ?? attempt.errorCategory ?? '—'}</td></tr>)}</tbody></Table></div>
        <Pages page={page} total={query.data.total} size={query.data.pageSize} change={setPage} />
      </> : <p>No delivery attempts have been recorded in this company.</p>}
  </Card>
}

