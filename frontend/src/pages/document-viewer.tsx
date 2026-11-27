import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { Badge, Button, Card, ErrorState, Field, Input, LoadingState, Table } from '@/components/ui'
import { api, errorMessage, manilaTimestamp } from '@/lib/account-api'
import { SyncJobPanel } from './accounting-sync'
import { DocumentReconciliationPanel } from './reconciliation'
import { documentLabels, documentsRoot, statusName, type DocumentDetail, type DocumentLine, type DocumentOptions, type DocumentType } from '@/lib/document-api'

const totalsLabels: Record<string, string> = {
  grossSales: 'Gross sales', commercialDiscount: 'Commercial discounts', statutoryDiscount: 'Statutory discounts',
  vatableSales: 'VATable sales', vatExemptSales: 'VAT-exempt sales', zeroRatedSales: 'Zero-rated sales', nonVatSales: 'Non-VAT sales',
  vat: 'Output VAT', total: 'Document total', incomeWithholding: 'Creditable withholding tax', vatWithholding: 'Withholding VAT',
  amountDue: 'Net amount due', rounding: 'Rounding adjustment',
}

export function DocumentTotals({ totals, currency }: { totals: Record<string, string>; currency: string }) {
  return <dl className="workspace-definitions" aria-label="Financial totals">{Object.entries(totalsLabels).map(([key, title]) =>
    <div key={key}><dt>{title}</dt><dd className="doc-amount">{currency} {totals[key]}</dd></div>)}</dl>
}

export function LineTable({ lines }: { lines: DocumentLine[] }) {
  return <div className="doc-table" role="region" aria-label="Document line breakdown" tabIndex={0}>
    <Table><thead><tr>{['Item', 'Description', 'Quantity', 'Unit price', 'Discount', 'VAT', 'Amount'].map(title => <th scope="col" key={title}>{title}</th>)}</tr></thead>
      <tbody>{lines.map(line => <tr key={line.id}><td>{line.itemCode}</td><td>{line.description}</td><td>{line.quantity} {line.unitCode}</td>
        <td>{line.unitPrice}</td><td>{line.commercialDiscount}{line.statutoryDiscount !== '0' && ' + ' + line.statutoryDiscount}</td>
        <td>{line.vat}</td><td>{line.total}</td></tr>)}</tbody>
    </Table>
  </div>
}

export function DocumentViewer({ base, type, id, options }: { base: string; type: DocumentType; id: string; options: DocumentOptions }) {
  const client = useQueryClient()
  const path = base + '/' + type + '/' + id
  const query = useQuery({ queryKey: ['fiscal-documents', base, 'detail', id], queryFn: () => api<DocumentDetail>(path), retry: false,
    refetchInterval: type === 'SalesOrder' ? false : 2500 })
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [recipient, setRecipient] = useState('')
  const [operation, setOperation] = useState('')
  if (query.isPending) return <LoadingState label="Loading permanent record…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  const result = query.data
  const doc = result.document
  const canResend = type !== 'SalesOrder' && options.canResend && options.development
  const working = ['PendingPdf', 'Queued', 'Sending'].includes(result.electronicDelivery)
  async function request(action: string, body?: unknown) {
    setBusy(true); setError(''); setNotice('')
    try {
      await api(path + '/' + action, 'POST', body)
      setOperation(''); setNotice('The operation is queued. The committed record and credit use are unchanged.')
      await client.invalidateQueries({ queryKey: ['fiscal-documents', base] })
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  const details: [string, React.ReactNode][] = [
    ['Customer', doc.customerCode + ' · ' + doc.customerName], ['Document date', doc.date], ['Due date', result.dueDate], ['Currency', doc.currency],
    ['Exchange rate', result.exchangeRate], ['Branch', result.parties.branchCode + ' · ' + result.parties.branchName],
    ['Credit consumption', doc.credits ? '1 credit consumed' : '0 credits'], ['PDF rendering', statusName(result.rendering)],
    ['Electronic delivery', statusName(result.electronicDelivery)], ['Accounting synchronization', statusName(result.accounting)],
    ['BIR reporting', statusName(result.regulator)],
  ]
  const sourceNumber = typeof result.header.sourceNumber === 'string' ? result.header.sourceNumber : doc.externalOrderNumber
  return <div className="doc-stack">
    <Card className="workspace-card doc-panel">
      <div className="doc-toolbar"><div><h2>{doc.number}</h2><p>{documentLabels[type]}</p></div>
        <div className="doc-actions"><Badge tone="success">Committed — Permanent Record</Badge>
          {doc.developmentFixture && <Badge tone="warning">Development fixture</Badge>}</div></div>
      <dl className="workspace-definitions">
        {details.map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}
        {sourceNumber && <div><dt>Source reference</dt><dd>{sourceNumber}</dd></div>}
        {result.file && <div><dt>PDF SHA-256</dt><dd>{result.file.sha256}</dd></div>}
        {result.templates.pdf && <div><dt>PDF template</dt><dd>{result.templates.pdf.name} · v{result.templates.pdf.version}</dd></div>}
        {result.templates.email && <div><dt>Email template</dt><dd>{result.templates.email.name} · v{result.templates.email.version}</dd></div>}
      </dl>
      <div className="doc-actions">
        <Link className="text-link" to={documentsRoot + '/' + type}>Back to register</Link>
        {result.file && <>
          <Button variant="light" asChild><a href={'/api' + path + '/pdf'} download>Download issued PDF</a></Button>
          <Button variant="light" asChild><a href={'/api' + path + '/pdf?inline=true'} target="_blank" rel="noreferrer">View or print PDF</a></Button>
        </>}
        {type === 'SalesInvoice' && options.canWrite && <Button variant="light" asChild><Link to={documentsRoot + '/CreditMemo/new?source=' + doc.id}>Create linked Credit Memo</Link></Button>}
        {type === 'SalesOrder' && options.canWrite && <Button variant="light" asChild><Link to={documentsRoot + '/SalesInvoice/new?source=' + doc.id}>Create invoice from order</Link></Button>}
      </div>
      <h3>Committed values</h3>
      <LineTable lines={result.lines} />
      <DocumentTotals totals={result.totals} currency={doc.currency} />
      <details><summary>Calculation and snapshot history</summary>
        <p>Seller {result.parties.seller.name} · Customer {result.parties.customer.name}</p>
        {result.parties.customer.tin && <p>Customer TIN {result.parties.customer.tin}</p>}
        {result.parties.customer.registeredAddress && <p>Registered address {result.parties.customer.registeredAddress}</p>}
        {result.lines.map(line => <details key={line.id}><summary>{line.itemCode} · Pinned calculation rules and terms</summary>
          <pre className="doc-preview-mails">{JSON.stringify(line.terms, null, 2)}</pre></details>)}
      </details>
    </Card>
    {type !== 'SalesOrder' && <StructuredInvoicePanel key={path} path={path + '/structured'} />}
    {doc.developmentFixture && <SyncJobPanel base={base.replace(/\/documents$/, '/connect')} id={doc.id} />}
    {doc.developmentFixture && <DocumentReconciliationPanel base={base.replace(/\/documents$/, '/connect/reconciliation')} id={doc.id} />}
    {!!result.related.length && <Card className="workspace-card doc-panel"><h3>Related documents</h3>
      <ul>{result.related.map(row => <li key={row.id}><Link className="text-link" to={documentsRoot + '/' + row.type + '/' + row.id}>{row.number}</Link>
        {' · ' + documentLabels[row.type] + ' · ' + row.date}</li>)}</ul></Card>}
    {type !== 'SalesOrder' && <Card className="workspace-card doc-panel">
      <h3>Electronic delivery</h3>
      {canResend && result.file && <div className="doc-grid">
        <Field id="resend-address" label="Authorized recipient correction"><Input id="resend-address" type="email" value={recipient}
          placeholder="Use original authorized address" onChange={e => { setRecipient(e.target.value); setOperation('') }} /></Field>
        <div><Button type="button" variant="light" disabled={busy || working} onClick={() => {
          const key = operation || crypto.randomUUID(); setOperation(key); void request('resend', { key, recipient: recipient || null })
        }}>Resend email</Button></div>
      </div>}
      {canResend && ['RequiresReview', 'Failed'].includes(result.rendering) &&
        <div><Button variant="light" disabled={busy} onClick={() => void request('retry-render', { key: crypto.randomUUID() })}>Retry original PDF</Button></div>}
      {error && <ErrorState>{error}</ErrorState>}
      {notice && <p role="status">{notice}</p>}
      {result.delivery.length ? <div className="doc-table" role="region" aria-label="Document delivery attempts" tabIndex={0}>
        <Table><thead><tr>{['Attempt', 'Time', 'Recipient', 'Status', 'Attachment SHA-256', 'Reference', 'Retry'].map(h => <th scope="col" key={h}>{h}</th>)}</tr></thead>
          <tbody>{result.delivery.map(attempt => {
            const latest = result.delivery.find(x => x.jobId === attempt.jobId)?.id === attempt.id
            return <tr key={attempt.id}><td>{attempt.attempt}</td><td>{manilaTimestamp.format(new Date(attempt.at))}</td>
              <td>{attempt.recipient}</td><td>{statusName(attempt.status)}</td><td>{attempt.attachmentSha256 ?? '—'}</td>
              <td>{attempt.providerReference ?? attempt.errorCategory ?? '—'}</td><td>{canResend && latest && ['Failed', 'RetryScheduled', 'RequiresReview'].includes(attempt.status)
                ? <Button variant="light" type="button" disabled={busy || working} onClick={() => void request('retry-delivery/' + attempt.jobId)}>Retry attempt</Button> : '—'}</td></tr>
          })}</tbody>
        </Table>
      </div> : <p>Delivery activity will appear after the PDF is rendered.</p>}
    </Card>}
    <Card className="workspace-card doc-panel">
      <h3>Audit history</h3>
      <div className="doc-table" role="region" aria-label="Document audit history" tabIndex={0}><Table>
        <thead><tr><th scope="col">Event</th><th scope="col">Time</th><th scope="col">Actor</th></tr></thead>
        <tbody>{result.audit.map((row, index) => <tr key={index}><td>{row.action}</td>
          <td>{manilaTimestamp.format(new Date(row.at))}</td><td>{row.actorId ?? 'System'}</td></tr>)}</tbody>
      </Table></div>
    </Card>
  </div>
}

type StructuredInvoice = {
  available: boolean
  notice: string
  schemaVersion?: string
  generatedAt?: string
  sha256?: string
  validation?: { internalSchema: 'Valid' | 'Invalid'; regulatoryEvidence: 'Captured' | 'Incomplete'; issues: string[] }
  data?: {
    originalFinancialSnapshotSha256: string
    fiscalSnapshot: {
      document: { currency: string; number: string; sourceNumber?: string | null; originalInvoiceId?: string | null }
      seller: { name: string; tradeName?: string | null; tin?: string | null; rdo?: string | null; vatStatus?: string | null
        branchCode: string; branchName: string; registeredAddress?: string | null }
      buyer: { name: string; tin?: string | null; classification?: string | null; registeredAddress?: string | null; billingAddress?: string | null }
      lines: (DocumentLine & { taxClassification: string | null })[]
      totals: Record<string, string>
    }
    documentedAuthorizationReferences: { id: string; kind: string; number: number; reference: string }[] | null
    evidenceLimitations: string[]
  }
}

function StructuredInvoicePanel({ path }: { path: string }) {
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const query = useQuery({ queryKey: ['structured-invoice', path], queryFn: () => api<StructuredInvoice>(path), enabled: open, retry: false })
  const result = query.data
  const evidence = result?.data
  const snapshot = evidence?.fiscalSnapshot

  async function generate() {
    setBusy(true); setError('')
    try {
      await api(path + '/generate', 'POST')
      await query.refetch()
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }

  async function verify() {
    setMessage('')
    const recent = await query.refetch()
    if (recent.data?.available && !recent.isError) setMessage('Both SHA-256 integrity checks passed.')
  }

  return <Card className="workspace-card doc-panel" aria-label="Structured electronic invoice">
    <h3>Structured electronic invoice</h3>
    <p>Internal structured electronic invoice — official BIR transmission format awaiting verified specifications.</p>
    <Button variant="light" onClick={() => setOpen(value => !value)} aria-expanded={open}>{open ? 'Hide structured data' : 'View Structured Data'}</Button>
    {open && <>
      {query.isPending && <LoadingState label="Loading structured invoice…" />}
      {query.isError && <ErrorState>{errorMessage(query.error)}</ErrorState>}
      {error && <ErrorState>{error}</ErrorState>}
      {result && !result.available && <div className="workspace-stack">
        <p>This legacy invoice contains only the original committed evidence. Generation will identify missing historical information.</p>
        <Button disabled={busy} onClick={() => void generate()}>Generate from committed record</Button>
      </div>}
      {!query.isError && evidence && snapshot && <>
        <div className="doc-actions">
          <Button variant="light" asChild><a href={'/api' + path + '/download'} download>Download JSON</a></Button>
          <Button variant="light" disabled={query.isFetching} onClick={() => void verify()}>Verify Integrity</Button>
        </div>
        {message && <p role="status">{message}</p>}
        <dl className="workspace-definitions">
          <div><dt>Canonical schema</dt><dd>{result?.schemaVersion}</dd></div>
          <div><dt>Structured data SHA-256</dt><dd>{result?.sha256}</dd></div>
          <div><dt>Fiscal snapshot SHA-256</dt><dd>{evidence.originalFinancialSnapshotSha256}</dd></div>
          <div><dt>Seller</dt><dd>{snapshot.seller.name}{snapshot.seller.tradeName && ' · ' + snapshot.seller.tradeName}</dd></div>
          <div><dt>Seller tax record</dt><dd>{[snapshot.seller.tin, snapshot.seller.rdo && 'RDO ' + snapshot.seller.rdo, snapshot.seller.vatStatus].filter(Boolean).join(' · ') || 'Uncaptured'}</dd></div>
          <div><dt>Seller registered address</dt><dd>{snapshot.seller.registeredAddress || 'Uncaptured'}</dd></div>
          <div><dt>Branch</dt><dd>{snapshot.seller.branchCode} · {snapshot.seller.branchName}</dd></div>
          <div><dt>Buyer</dt><dd>{[snapshot.buyer.name, snapshot.buyer.tin, snapshot.buyer.classification].filter(Boolean).join(' · ')}</dd></div>
          {(snapshot.buyer.registeredAddress || snapshot.buyer.billingAddress) &&
            <div><dt>Buyer address</dt><dd>{snapshot.buyer.registeredAddress || snapshot.buyer.billingAddress}</dd></div>}
          {snapshot.document.sourceNumber && <div><dt>Source reference</dt><dd>{snapshot.document.sourceNumber}</dd></div>}
        </dl>
        <LineTable lines={snapshot.lines} />
        <ul aria-label="Pinned line tax classifications">{snapshot.lines.map(line =>
          <li key={line.id}>{line.itemCode} · {line.taxClassification || 'Tax classification uncaptured'}</li>)}</ul>
        <DocumentTotals totals={snapshot.totals} currency={snapshot.document.currency} />
        <details><summary>View Validation Results</summary>
          <p>Internal schema {result?.validation?.internalSchema === 'Valid' ? 'valid' : 'invalid'}. Regulatory evidence {result?.validation?.regulatoryEvidence.toLowerCase()}.</p>
          <p>Official schema unavailable. Official validation has not been performed.</p>
          {result?.validation?.issues.map(issue => <p key={issue}>{issue}</p>)}
          {evidence.evidenceLimitations.map(limitation => <p key={limitation}>{limitation}</p>)}
          {evidence.documentedAuthorizationReferences?.map(reference =>
            <p key={reference.id}>{reference.kind} · {reference.reference} · version {reference.number}</p>)}
        </details>
        <details className="reporting-evidence"><summary>View JSON</summary>
          <pre>{JSON.stringify(evidence, null, 2)}</pre>
        </details>
      </>}
    </>}
  </Card>
}

