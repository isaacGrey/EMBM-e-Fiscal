import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { Badge, Button, Card, Dialog, DialogContent, ErrorState, Field, Input, Select } from '@/components/ui'
import { api, apiBlob, ApiError, errorMessage, manilaDate } from '@/lib/account-api'
import type { MasterFields, MasterMenu } from '@/lib/master-data'
import {
  documentLabels, documentsRoot, procedureName, type DocumentOptions, type DocumentPreview, type DocumentReceipt,
  type DocumentRegisterData, type DocumentType, type EmailPreview, type ExternalSource, type SourceDocument,
} from '@/lib/document-api'
import { Reference } from './master-fields'
import { blankLine, DiscountSelections, DocumentCustomFields, DocumentLineFields, type LineDraft } from './document-line-fields'
import { DocumentTotals, LineTable } from './document-viewer'

const optional = (value: string) => value.trim() || null
const leaveWarning = 'Leave this form? Uncommitted document inputs will be lost.'
export const mayChangeDocumentContext = () => window.dispatchEvent(new Event('efiscal-document-context', { cancelable: true }))
const emptyHeader = { remarks: '', adjustmentReason: '', adjustmentKind: 'Return', requestedDeliveryDate: '', shippingAddress: '',
  salesperson: '', purchaseOrderReference: '', externalReference: '', warehouseReference: '' }

export function DocumentForm({ base, master, type, options, branch, source, externalKey, onCommitted }: {
  base: string; master: string; type: DocumentType; options: DocumentOptions; branch?: string; source: string; externalKey: string
  onCommitted: (result: DocumentReceipt) => void
}) {
  const order = type === 'SalesOrder'
  const credit = type === 'CreditMemo'
  const sourceType = credit ? 'SalesInvoice' : 'SalesOrder'
  const path = base + '/' + type
  const client = useQueryClient()
  const incoming = type === 'SalesInvoice' && options.development ? externalKey : ''
  const [sourcing, setSourcing] = useState(incoming ? 'external' : credit || source ? 'internal' : 'direct')
  const [sourceId, setSourceId] = useState(source)
  const [sourceSearch, setSourceSearch] = useState('')
  const [date, setDate] = useState(manilaDate)
  const [branchId, setBranchId] = useState(branch ?? options.branches[0]?.id ?? '')
  const [customerId, setCustomerId] = useState('')
  const [currency, setCurrency] = useState('')
  const [priceListId, setPriceListId] = useState('')
  const [paymentTermId, setPaymentTermId] = useState('')
  const [allowPartial, setAllowPartial] = useState(false)
  const [header, setHeader] = useState(emptyHeader)
  const [custom, setCustom] = useState<MasterFields>({})
  const [lines, setLines] = useState<LineDraft[]>(credit || source || incoming ? [] : [blankLine(1)])
  const [discounts, setDiscounts] = useState<string[]>([])
  const [pdfTemplate, setPdfTemplate] = useState('')
  const [emailTemplate, setEmailTemplate] = useState('')
  const [recipient, setRecipient] = useState('')
  const [review, setReview] = useState<DocumentPreview>()
  const [authorizedRecipients, setAuthorizedRecipients] = useState<string[]>([])
  const [email, setEmail] = useState<EmailPreview>()
  const [pdf, setPdf] = useState('')
  const [confirm, setConfirm] = useState(false)
  const [dirty, setDirty] = useState(false)
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [uncertain, setUncertain] = useState(false)
  const operation = useRef('')
  const nextLine = useRef(2)
  const menu = useQuery({ queryKey: ['master-data', master, 'menu'], queryFn: () => api<MasterMenu>(master), retry: false })
  const pick = useQuery({ queryKey: ['fiscal-documents', base, 'source-lookup', sourceType, sourceSearch],
    queryFn: () => api<DocumentRegisterData>(base + '/' + sourceType + '/register?q=' + encodeURIComponent(sourceSearch)),
    enabled: sourcing === 'internal', retry: false })
  const linked = useQuery({ queryKey: ['fiscal-documents', base, 'source', sourceType, sourceId],
    queryFn: () => api<SourceDocument>(base + '/' + sourceType + '/' + sourceId + '/source-lines'), enabled: sourcing === 'internal' && !!sourceId, retry: false })
  const sourceKey = incoming || 'sample-order'
  const external = useQuery({ queryKey: ['fiscal-documents', base, 'external-source', sourceKey],
    queryFn: () => api<ExternalSource>(base + '/external-order/' + encodeURIComponent(sourceKey)), enabled: sourcing === 'external', retry: false })
  const seeded = useRef(false)
  useEffect(() => {
    const order = external.data
    if (!incoming || !order || seeded.current || sourcing !== 'external') return
    seeded.current = true
    if (order.branchId) setBranchId(order.branchId)
    const mapped = order.lines.filter(line => Number(line.remainingQuantity) > 0)
    setLines(mapped.map((line, index) => ({ ...blankLine(index + 1), itemId: line.itemId,
      unitId: line.unitId, quantity: line.remainingQuantity, externalLineKey: line.key })))
    nextLine.current = mapped.length + 1
  }, [incoming, external.data, sourcing])
  const amountOnly = credit && header.adjustmentKind === 'PriceAdjustment'

  useEffect(() => {
    if (!dirty) return
    function pageLeave(event: BeforeUnloadEvent) { event.preventDefault(); event.returnValue = '' }
    function routeChange(event: Event) { if (!window.confirm(leaveWarning)) event.preventDefault() }
    function linkClick(event: MouseEvent) {
      const anchor = event.target instanceof Element ? event.target.closest('a[href]') : null
      if (!anchor || anchor.getAttribute('target') || anchor.hasAttribute('download') || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return
      const next = new URL(anchor.getAttribute('href')!, window.location.href)
      if (next.origin === window.location.origin && next.pathname !== window.location.pathname && !window.confirm(leaveWarning)) {
        event.preventDefault(); event.stopPropagation()
      }
    }
    window.addEventListener('beforeunload', pageLeave)
    window.addEventListener('efiscal-document-context', routeChange)
    document.addEventListener('click', linkClick, true)
    return () => {
      window.removeEventListener('beforeunload', pageLeave)
      window.removeEventListener('efiscal-document-context', routeChange)
      document.removeEventListener('click', linkClick, true)
    }
  }, [dirty])
  useEffect(() => () => { if (pdf) URL.revokeObjectURL(pdf) }, [pdf])

  function modify() {
    setDirty(true); setReview(undefined); setEmail(undefined); setPdf(''); setError('')
    operation.current = ''
  }
  function changeSource(value: string) {
    modify(); setSourcing(value); setSourceId(''); setCustomerId(''); setAuthorizedRecipients([]); setRecipient('')
    setLines(value === 'direct' ? [blankLine(nextLine.current++)] : [])
  }
  function changeHeader(key: keyof typeof header, value: string) {
    modify(); setHeader(previous => ({ ...previous, [key]: value }))
  }
  function append(line: Partial<LineDraft> = {}) {
    modify(); setLines(previous => [...previous, { ...blankLine(nextLine.current++), ...line }])
  }
  function input() {
    return {
      action: order ? 'CommitOnly' : 'CommitAndSend', date, branchId,
      customerId: sourcing === 'direct' ? optional(customerId) : linked.data?.document.customerId ?? external.data?.customerId ?? null,
      currency: optional(currency), priceListId: credit ? null : optional(priceListId), paymentTermId: credit ? null : optional(paymentTermId),
      sourceDocumentId: sourcing === 'internal' ? optional(sourceId) : null, externalOrderKey: sourcing === 'external' ? external.data?.key : null,
      documentDiscountIds: !credit ? discounts : null, recipient: order ? null : optional(recipient),
      pdfTemplateId: order ? null : optional(pdfTemplate), emailTemplateId: order ? null : optional(emailTemplate),
      remarks: optional(header.remarks), adjustmentReason: credit ? optional(header.adjustmentReason) : null,
      adjustmentKind: credit ? header.adjustmentKind : null, requestedDeliveryDate: order ? optional(header.requestedDeliveryDate) : null,
      shippingAddress: order ? optional(header.shippingAddress) : null, salesperson: optional(header.salesperson),
      purchaseOrderReference: optional(header.purchaseOrderReference), externalReference: optional(header.externalReference),
      warehouseReference: order ? optional(header.warehouseReference) : null, allowPartial: order && allowPartial, custom,
      lines: lines.map(line => ({
        itemId: line.itemId || '00000000-0000-0000-0000-000000000000', unitId: optional(line.unitId),
        quantity: amountOnly ? null : optional(line.quantity), unitPrice: credit ? null : optional(line.unitPrice),
        taxGroupId: credit ? null : optional(line.taxGroupId), discountGroupIds: credit ? null : line.discountGroupIds,
        eligibility: !credit && line.eligibility.category ? { ...line.eligibility, quantity: optional(line.eligibility.quantity) } : null,
        sourceLineId: optional(line.sourceLineId), externalLineKey: optional(line.externalLineKey),
        adjustmentAmount: amountOnly ? optional(line.adjustmentAmount) : null, description: optional(line.description),
        projectId: optional(line.projectId), departmentId: optional(line.departmentId), locationId: optional(line.locationId),
        accountId: optional(line.accountId), custom: line.custom,
      })),
    }
  }
  async function preview(kind: 'totals' | 'pdf' | 'email') {
    setBusy(kind); setError('')
    try {
      if (kind === 'totals') {
        const result = await api<DocumentPreview>(path + '/preview', 'POST', input())
        setReview(result); setAuthorizedRecipients(result.recipients)
      } else if (kind === 'pdf') {
        const bytes = await apiBlob(path + '/pdf-preview', 'POST', input())
        setPdf(URL.createObjectURL(bytes))
      } else setEmail(await api<EmailPreview>(path + '/email-preview', 'POST', input()))
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy('') }
  }
  async function done(doc: DocumentReceipt) {
    setDirty(false); setUncertain(false); setConfirm(false)
    await Promise.all([client.invalidateQueries({ queryKey: ['fiscal-documents', base] }), client.invalidateQueries({ queryKey: ['commercial'] })])
    onCommitted(doc)
  }
  async function recover() {
    const result = await api<DocumentReceipt>(base + '/commit-result/' + operation.current)
    await done(result)
  }
  async function commit() {
    if (!review) return
    operation.current ||= crypto.randomUUID()
    setConfirm(false); setBusy('commit'); setError('')
    try {
      const result = await api<DocumentReceipt>(path + '/commit', 'POST', { key: operation.current, confirmation: review.confirmation, document: input() })
      await done(result)
    } catch (failure) {
      if (!(failure instanceof ApiError) || failure.status >= 500) {
        try { await recover() }
        catch { setUncertain(true); setError('Commitment status is uncertain. Check the result or retry with the same operation key.') }
      } else setError(errorMessage(failure))
    } finally { setBusy('') }
  }

  const canReview = lines.length > 0 && !!branchId && !!date && (sourcing === 'direct' ? !!customerId : !!linked.data || !!external.data)
  const ready = !!review && options.canCommit && review.seriesConfigured && (order || review.senderConfigured && !!review.recipient && (review.availableCredits ?? 0) > 0)
  const label = procedureName(type)
  const submit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); void preview('totals') }
  const lookup = (kind: 'pdf' | 'email', selected: string, set: (value: string) => void) =>
    <Field id={'doc-template-' + kind} label={kind === 'pdf' ? 'PDF template' : 'Email template'}>
      <Select id={'doc-template-' + kind} value={selected} onChange={e => { modify(); set(e.target.value) }}>
        <option value="">Company default</option>{options.templates.filter(t => t.kind === kind).map(x =>
          <option value={x.id} key={x.id}>{x.name} · v{x.currentVersion}{x.isDefault ? ' · Default' : ''}</option>)}</Select>
    </Field>

  return <div className="doc-stack">
    <Card className="workspace-card doc-panel">
      <div className="doc-toolbar"><h2>New {documentLabels[type]}</h2>
        {!order && <Badge tone="warning">{review?.availableCredits ?? options.availableCredits ?? 0} Save &amp; Send Credits available</Badge>}</div>
      {!options.canCommit && <p className="doc-warning">Production issuance requires verified authorizations. Preview is available.</p>}
      {options.development && !order && <p className="doc-note">Development documents use approved .test recipients and a local mailbox.</p>}
      {!order && options.canBuyCredits && <div><a className="text-link" href="/portal/client/credits#buy-credits" target="_blank" rel="noreferrer">
        Buy Credits in a new tab</a></div>}
      <form className="doc-form" onSubmit={submit}>
        <fieldset className="doc-stack doc-fieldset" disabled={!!busy || uncertain}>
          <div className="doc-grid">
            <Field id="doc-branch" label="Document branch"><Select id="doc-branch" value={branchId} required onChange={e => { modify(); setBranchId(e.target.value) }}>
              <option value="">Select branch</option>{options.branches.map(x => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}</Select></Field>
            <Field id="doc-date" label="Document date"><Input id="doc-date" type="date" value={date} onChange={e => { modify(); setDate(e.target.value) }} required /></Field>
            {!order && !credit && <Field id="doc-source-mode" label="Invoice source"><Select id="doc-source-mode" value={sourcing} onChange={e => changeSource(e.target.value)}>
              <option value="direct">Direct invoice</option><option value="internal">Committed eFiscal Sales Order</option>
              {options.development && <option value="external">{incoming ? 'Connected source order' : 'External Sales Order development fixture'}</option>}</Select></Field>}
            {sourcing === 'direct' && <Reference base={master} id="doc-customer" label="Customer" kind="customers" value={customerId} required
              onChange={value => { modify(); setCustomerId(value); setRecipient(''); setAuthorizedRecipients([]) }} />}
            {!credit && <>
              <Field id="doc-currency" label="Currency code"><Input id="doc-currency" maxLength={3} placeholder="Customer default or PHP" value={currency}
                onChange={e => { modify(); setCurrency(e.target.value) }} /></Field>
              <Reference base={master} id="doc-price-list" label="Price list" kind="price-lists" value={priceListId}
                onChange={value => { modify(); setPriceListId(value) }} emptyLabel="Company defaults" />
              <Reference base={master} id="doc-payment-term" label="Payment terms" kind="payment-terms" value={paymentTermId}
                onChange={value => { modify(); setPaymentTermId(value) }} emptyLabel="Customer defaults" />
            </>}
            {credit && <Field id="adjustment-kind" label="Adjustment type"><Select id="adjustment-kind" value={header.adjustmentKind}
              onChange={e => changeHeader('adjustmentKind', e.target.value)}><option value="Return">Quantity return</option>
              <option value="PriceAdjustment">Price adjustment amount</option></Select></Field>}
          </div>
          {sourcing === 'internal' && <div className="doc-stack">
            <div className="doc-grid">
              <Field id="doc-source-search" label={'Search ' + documentLabels[sourceType]}><Input id="doc-source-search" type="search"
                value={sourceSearch} placeholder="Document number or customer" onChange={e => setSourceSearch(e.target.value)} /></Field>
              <Field id="doc-source" label={credit ? 'Original Sales Invoice' : 'Committed Sales Order'}>
                <Select id="doc-source" value={sourceId} required onChange={e => {
                  modify(); setSourceId(e.target.value); setLines([]); setRecipient(''); setAuthorizedRecipients([])
                }}>
                  <option value="">Select source document</option>
                  {linked.data && !pick.data?.items.some(item => item.document.id === sourceId) &&
                    <option value={sourceId}>{linked.data.document.number} · {linked.data.document.customerName}</option>}
                  {pick.data?.items.map(({ document }) => <option key={document.id} value={document.id}>{document.number} · {document.customerName}</option>)}
                </Select>
              </Field>
            </div>
            {(pick.isError || linked.isError) && <ErrorState>{errorMessage(pick.error ?? linked.error)}</ErrorState>}
            {linked.data && <>
              <p>{linked.data.document.number} · {linked.data.document.customerName} · {linked.data.document.currency}</p>
              <div className="doc-actions">{linked.data.lines.map(line => <Button type="button" variant="light" key={line.id}
                disabled={lines.some(row => row.sourceLineId === line.id) || (credit && amountOnly ? Number(line.remainingAmount) <= 0 : Number(line.remainingQuantity) <= 0)}
                onClick={() => append({ itemId: line.itemId, unitId: line.unitId, sourceLineId: line.id, quantity: line.remainingQuantity })}>
                Add {line.itemCode} from source</Button>)}</div>
            </>}
          </div>}
          {sourcing === 'external' && (external.isError ? <ErrorState>{errorMessage(external.error)}</ErrorState> : external.data && <div className="doc-stack">
            <p>{external.data.number} · {external.data.connector}</p>{external.data.financialReview && <ErrorState>{external.data.financialReview}</ErrorState>}
            <div className="doc-actions">{external.data.lines.map(line =>
              <Button type="button" variant="light" key={line.key} disabled={Number(line.remainingQuantity) <= 0 || lines.some(row => row.externalLineKey === line.key)}
                onClick={() => append({ itemId: line.itemId, unitId: line.unitId, quantity: line.remainingQuantity, externalLineKey: line.key })}>{incoming ? 'Add connected source line' : 'Add external fixture line'}</Button>)}</div></div>)}
          {credit && <Field id="doc-reason" label="Adjustment reason"><Input id="doc-reason" value={header.adjustmentReason} required maxLength={500}
            onChange={e => changeHeader('adjustmentReason', e.target.value)} /></Field>}
          <details><summary>Document references and notes</summary><div className="doc-grid">
            {(['salesperson', 'purchaseOrderReference', 'externalReference'] as const).map((key, index) =>
              <Field key={key} id={'doc-' + key} label={['Salesperson', 'Purchase order reference', 'External reference'][index]}>
                <Input id={'doc-' + key} value={header[key]} maxLength={120} onChange={e => changeHeader(key, e.target.value)} /></Field>)}
            <Field id="doc-remarks" label="Remarks"><textarea id="doc-remarks" rows={2} maxLength={1000} value={header.remarks}
              onChange={e => changeHeader('remarks', e.target.value)} /></Field>
            {order && <>
              <Field id="doc-requested-date" label="Requested delivery date"><Input id="doc-requested-date" type="date" value={header.requestedDeliveryDate}
                onChange={e => changeHeader('requestedDeliveryDate', e.target.value)} /></Field>
              <Field id="doc-ship" label="Shipping address"><textarea id="doc-ship" rows={2} maxLength={600} value={header.shippingAddress}
                onChange={e => changeHeader('shippingAddress', e.target.value)} /></Field>
              <Field id="doc-warehouse" label="Warehouse reference"><Input id="doc-warehouse" value={header.warehouseReference} maxLength={100}
                onChange={e => changeHeader('warehouseReference', e.target.value)} /></Field>
            </>}
          </div></details>
          {order && <label className="doc-check"><input type="checkbox" checked={allowPartial} onChange={e => { modify(); setAllowPartial(e.target.checked) }} />
            Allow partial invoicing</label>}
          <DocumentCustomFields id="doc-custom" type={type} line={false} values={custom} change={value => { modify(); setCustom(value) }} master={master} menu={menu.data} />
          <h3>Document lines</h3>
          {lines.map((line, index) => <DocumentLineFields key={line.key} line={line} index={index} type={type} master={master} menu={menu.data}
            linked={sourcing === 'internal'} source={linked.data?.lines.find(x => x.id === line.sourceLineId)} amountOnly={amountOnly}
            change={value => { modify(); setLines(current => current.map(item => item.key === line.key ? { ...item, ...value } : item)) }}
            remove={lines.length > 1 || sourcing !== 'direct' ? () => { modify(); setLines(previous => previous.filter(x => x.key !== line.key)) } : undefined} />)}
          {sourcing === 'direct' && <div><Button type="button" variant="light" disabled={lines.length >= 100} onClick={() => append()}>Add line</Button></div>}
          {!credit && <DiscountSelections id="doc-document-discounts" label="Document discount" base={master} values={discounts}
            change={values => { modify(); setDiscounts(values) }} />}
          {!order && <div className="doc-grid">{lookup('pdf', pdfTemplate, setPdfTemplate)}{lookup('email', emailTemplate, setEmailTemplate)}
            <Field id="doc-recipient" label="Authorized recipient"><Select id="doc-recipient" value={recipient}
              onChange={e => { modify(); setRecipient(e.target.value) }}><option value="">Use customer default</option>
              {authorizedRecipients.map(address => <option key={address} value={address}>{address}</option>)}</Select></Field></div>}
          <div className="doc-actions">
            <Button type="submit" disabled={!canReview}>{busy === 'totals' ? 'Calculating…' : 'Calculate totals'}</Button>
            {!order && <>
              <Button type="button" variant="light" disabled={!canReview} onClick={() => void preview('pdf')}>Preview PDF</Button>
              <Button type="button" variant="light" disabled={!canReview} onClick={() => void preview('email')}>Preview email</Button>
            </>}
            {pdf && <a className="text-link" href={pdf} download={type + '-preview.pdf'}>Download preview PDF</a>}
          </div>
        </fieldset>
      </form>
      {error && <ErrorState>{error}</ErrorState>}
      {uncertain && <div className="doc-actions">
        <Button variant="light" disabled={!!busy} onClick={() => void recover().catch(() => setError('A committed result is not available yet.'))}>Check commitment result</Button>
        <Button disabled={!!busy} onClick={() => void commit()}>Retry same operation</Button>
      </div>}
    </Card>
    {email && <Card className="workspace-card doc-panel">
      <h3>Email preview</h3><p>Recipient {email.recipient ?? 'Select an authorized address'}</p><strong>{email.subject}</strong>
      <p className="doc-preview-mails">{[email.brand, email.body, email.signature, email.footer].filter(Boolean).join('\n\n')}</p>
    </Card>}
    {review && <Card className="workspace-card doc-panel">
      <h3>Calculated document totals</h3><p>{review.customerName} · {review.currency} · Due {review.dueDate} · Rate {review.exchangeRate}</p>
      {review.parties.customer.tin && <p>TIN {review.parties.customer.tin}{review.parties.customer.rdo ? ' · RDO ' + review.parties.customer.rdo : ''}</p>}
      {review.parties.customer.billingAddress && <p>Billing address {review.parties.customer.billingAddress}</p>}
      <LineTable lines={review.lines} /><DocumentTotals totals={review.totals} currency={review.currency} />
      {!review.seriesConfigured && <p className="doc-warning">This type needs an effective numbering series. <Link className="text-link" to={documentsRoot + '/numbering'} target="_blank">
        Open numbering in a new tab</Link></p>}
      {!order && !review.senderConfigured && <p className="doc-warning">Save a local platform sender before committing.</p>}
      {!order && (review.availableCredits ?? 0) < 1 && <p className="doc-warning">Available credits are insufficient. The form stays open while you buy credits in another tab.</p>}
      <div><Button disabled={!ready || !!busy || uncertain} onClick={() => order ? void commit() : setConfirm(true)}>{label}</Button></div>
    </Card>}
    {review && !order && <Dialog open={confirm} onOpenChange={setConfirm}>
      <DialogContent title={'Review ' + label} description="This document will be permanently committed and cannot be edited or deleted.">
        <dl className="workspace-definitions">
          {[
            ['Document type', documentLabels[type]], ['Customer', review.customerName], ['Date', review.date],
            ['Calculated total', review.currency + ' ' + review.totals.total], ['Authorized recipient', review.recipient ?? '—'],
            ['PDF template', review.pdfTemplate?.name ?? '—'], ['Email template', review.emailTemplate?.name ?? '—'],
            ['Available credits', review.availableCredits], ['Credits to consume', review.credits], ['Remaining credits', review.remainingCredits],
          ].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}
        </dl><div className="doc-actions">
          <Button type="button" variant="light" onClick={() => setConfirm(false)}>Cancel</Button>
          <Button type="button" onClick={() => void commit()}>Confirm Save &amp; Send</Button>
        </div>
      </DialogContent>
    </Dialog>}
  </div>
}

