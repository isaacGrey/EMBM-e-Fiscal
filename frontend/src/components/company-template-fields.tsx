import { useEffect, useState } from 'react'
import { ArrowDown, ArrowUp } from 'lucide-react'
import { Button, Card, Field, Input, Select, Table } from './ui'
import { apiBlob, errorMessage } from '@/lib/account-api'
import { ErrorState } from './ui'
import type { DocumentType, EmailSample, EmailTemplate, PdfLayout } from '@/lib/company-configuration'

const columns: Record<string, { label: string; example: string }> = {
  description: { label: 'Description', example: 'Sample item' },
  quantity: { label: 'Quantity', example: '2' },
  unitPrice: { label: 'Unit price', example: '₱450.00' },
  discount: { label: 'Discount', example: '₱0.00' },
  tax: { label: 'Tax', example: '₱108.00' },
  amount: { label: 'Amount', example: '₱1,008.00' },
}

const emailTextFields = [
  ['brand', 'Branding text', 120], ['signature', 'Email signature', 500], ['footer', 'Email footer', 500],
] as const

export function EmailTemplateFields({ value, update, variables }: { value: EmailTemplate; update: (value: EmailTemplate) => void; variables: string[] }) {
  const [field, setField] = useState('')
  const [destination, setDestination] = useState<'subject' | 'body'>('body')
  return <>
    <Field id="template-subject" label="Email subject"><Input id="template-subject" required maxLength={160}
      value={value.subject} onChange={e => update({ ...value, subject: e.target.value })} /></Field>
    <Field id="template-body" label="Email body">
      <textarea id="template-body" className="control" rows={5} required maxLength={6000}
        value={value.body} onChange={e => update({ ...value, body: e.target.value })} />
    </Field>
    <details className="template-variables"><summary>Allowed variables</summary>
      <p>Insert a supported variable into the plain-text template.</p>
      <div className="form-grid">
        <Field id="email-variable" label="Dynamic field"><Select id="email-variable" value={field} onChange={event => setField(event.target.value)}>
          <option value="">Select a field</option>{variables.map(variable => <option value={variable} key={variable}>{variable}</option>)}</Select></Field>
        <Field id="email-variable-target" label="Insert into"><Select id="email-variable-target" value={destination}
          onChange={event => setDestination(event.target.value as 'subject' | 'body')}><option value="body">Email body</option><option value="subject">Subject</option></Select></Field>
      </div>
      <Button variant="light" type="button" disabled={!field} onClick={() => update({ ...value, [destination]: value[destination] + '{{' + field + '}}' })}>Insert dynamic field</Button>
    </details>
    {emailTextFields.map(([key, label, maxLength]) => <Field id={'template-' + key} label={label} key={key}>
      <textarea className="control" id={'template-' + key} maxLength={maxLength} rows={2}
        value={value[key] ?? ''} onChange={e => update({ ...value, [key]: e.target.value })} />
    </Field>)}
    <label className="acknowledgement"><input type="checkbox" checked={value.includeConfiguredCopies}
      onChange={e => update({ ...value, includeConfiguredCopies: e.target.checked })} /> Include company-approved CC and BCC</label>
  </>
}

export function EmailSampleCard({ sample }: { sample: EmailSample }) {
  return <Card className="workspace-card"><h2>Email preview</h2>
    <p>Fictional recipient · {sample.recipient}</p>
    <p><strong>{sample.subject}</strong></p>
    {sample.brand && <p><strong>{sample.brand}</strong></p>}
    <div className="mail-preview-body">{sample.body}</div>
    {sample.signature && <p className="mail-preview-body">{sample.signature}</p>}
    {sample.footer && <p className="mail-preview-body">{sample.footer}</p>}
  </Card>
}

export function LayoutTemplateFields({ value, update, setError }: { value: PdfLayout; update: (value: PdfLayout) => void; setError: (value: string) => void }) {
  function move(index: number, offset: number) {
    const arranged = [...value.columns]
    const target = index + offset
    ;[arranged[index], arranged[target]] = [arranged[target], arranged[index]]
    const widths = value.columnWeights ? [...value.columnWeights] : [36, 11, 14, 12, 12, 15]
    ;[widths[index], widths[target]] = [widths[target], widths[index]]
    update({ ...value, columns: arranged, columnWeights: widths })
  }
  function readLogo(file: File | undefined) {
    if (!file) return
    if (file.type !== 'image/png' || file.size > 65_536) {
      setError('Choose a PNG logo up to 64 KB.')
      return
    }
    const reader = new FileReader()
    reader.onload = () => { if (typeof reader.result === 'string') update({ ...value, logo: reader.result }) }
    reader.onerror = () => setError('The logo could not be read.')
    reader.readAsDataURL(file)
  }
  return <>
    <div className="form-grid">
      <Field id="layout-title" label="Document title"><Input id="layout-title" required maxLength={100}
        value={value.title} onChange={e => update({ ...value, title: e.target.value })} /></Field>
      <Field id="layout-header" label="Header text"><Input id="layout-header" maxLength={180}
        value={value.header ?? ''} onChange={e => update({ ...value, header: e.target.value })} /></Field>
      {([['businessLayout', 'Registered business layout'], ['customerLayout', 'Customer information layout']] as const).map(([key, label]) =>
        <Field id={key} label={label} key={key}><Select id={key} value={value[key]} onChange={e => update({ ...value, [key]: e.target.value })}>
          <option value="split">Two columns</option><option value="stacked">Stacked</option>
        </Select></Field>)}
      <Field id="layout-font" label="Font style"><Select id="layout-font" value={value.font} onChange={e => update({ ...value, font: e.target.value })}>
        <option value="sans">Sans serif</option><option value="serif">Serif</option><option value="mono">Monospace</option>
      </Select></Field>
      <Field id="layout-color" label="Accent color"><Input id="layout-color" type="color" value={value.accentColor}
        onChange={e => update({ ...value, accentColor: e.target.value })} /></Field>
      <Field id="layout-margin" label="Page margin (mm)"><Input id="layout-margin" type="number" min={12} max={32} required
        value={value.marginMm} onChange={e => update({ ...value, marginMm: Number(e.target.value) })} /></Field>
      <Field id="layout-size" label="Page size"><Select id="layout-size" value={value.pageSize ?? 'A4'} onChange={e => update({ ...value, pageSize: e.target.value as 'A4' | 'Letter' })}><option value="A4">A4</option><option value="Letter">Letter</option></Select></Field>
      <Field id="layout-font-size" label="Font size"><Input id="layout-font-size" type="number" min={8} max={12} value={value.fontSize ?? 10} onChange={e => update({ ...value, fontSize: Number(e.target.value) })} /></Field>
      <Field id="layout-summary" label="Financial summary position"><Select id="layout-summary" value={value.summaryPosition ?? 'right'} onChange={e => update({ ...value, summaryPosition: e.target.value as 'left' | 'right' })}><option value="right">Right</option><option value="left">Left</option></Select></Field>
      <Field id="layout-logo" label="Company logo (PNG)"><Input id="layout-logo" type="file" accept="image/png" onChange={e => readLogo(e.target.files?.[0])} /></Field>
    </div>
    {value.logo && <button type="button" className="text-link" onClick={() => update({ ...value, logo: null })}>Remove company logo</button>}
    <fieldset className="layout-column-settings"><legend>Line item column order and widths</legend>
      <p>Set column width percentages that add up to 100.</p>
      <ol>{value.columns.map((key, index) => <li key={key}><span>{columns[key].label}</span>
        <Input type="number" min={9} max={55} aria-label={columns[key].label + ' width percentage'} value={(value.columnWeights ?? [36, 11, 14, 12, 12, 15])[index]}
          onChange={e => { const widths = [...(value.columnWeights ?? [36, 11, 14, 12, 12, 15])]; widths[index] = Number(e.target.value); update({ ...value, columnWeights: widths }) }} />
        <Button variant="light" type="button" disabled={index === 0} aria-label={'Move ' + columns[key].label + ' earlier'} onClick={() => move(index, -1)}><ArrowUp size={13} /></Button>
        <Button variant="light" type="button" disabled={index === value.columns.length - 1} aria-label={'Move ' + columns[key].label + ' later'} onClick={() => move(index, 1)}><ArrowDown size={13} /></Button>
      </li>)}</ol>
    </fieldset>
    {([['paymentInstructions', 'Payment instructions', 500], ['terms', 'Terms and conditions', 800], ['footer', 'Document footer', 500]] as const).map(([key, label, maxLength]) =>
      <Field id={'layout-' + key} label={label} key={key}><textarea className="control" id={'layout-' + key} rows={2}
        maxLength={maxLength} value={value[key] ?? ''} onChange={e => update({ ...value, [key]: e.target.value })} /></Field>)}
    <label className="acknowledgement"><input type="checkbox" checked={value.signatureField} onChange={e => update({ ...value, signatureField: e.target.checked })} /> Include signature field</label>
    <label className="acknowledgement"><input type="checkbox" checked={value.pageNumbers} onChange={e => update({ ...value, pageNumbers: e.target.checked })} /> Show page numbers</label>
  </>
}

function DisplayDetails({ layout, value }: { layout: string; value: [string, string][] }) {
  return <dl className={'template-preview-details ' + (layout === 'split' ? 'preview-details-split' : '')}>
    {value.map(([label, item]) => <div key={label}><dt>{label}</dt><dd>{item}</dd></div>)}
  </dl>
}

export function LayoutSampleCard({ value, type, base }: { value: PdfLayout; type: DocumentType; base: string }) {
  const [file, setFile] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  useEffect(() => () => { if (file) URL.revokeObjectURL(file) }, [file])
  async function renderSample() {
    setBusy(true); setError(''); setFile('')
    try {
      const path = base.replace(/\/configuration$/, '') + '/documents/template-preview/' + (type === 'invoice' ? 'SalesInvoice' : 'CreditMemo')
      const result = await apiBlob(path, 'POST', value)
      setFile(URL.createObjectURL(result))
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  return <Card className="workspace-card template-layout-card"><h2>Layout preview</h2>
    <p>Fictional sample for the selected template.</p>
    <div className="doc-actions"><Button type="button" variant="light" disabled={busy} onClick={() => void renderSample()}>Preview PDF</Button>
      {file && <a className="text-link" href={file} download={type + '-template-preview.pdf'}>Download sample PDF</a>}</div>
    {error && <ErrorState>{error}</ErrorState>}
    <article className={'template-paper template-font-' + value.font} style={{ borderTopColor: value.accentColor, padding: Math.max(12, Math.min(32, value.marginMm)) }}>
      {value.logo && <img src={value.logo} alt="Company logo" className="template-logo" />}
      <h3>{type === 'invoice' ? 'Sales Invoice' : 'Credit Memo'}</h3>
      {value.title && value.title !== (type === 'invoice' ? 'Sales Invoice' : 'Credit Memo') && <p>{value.title}</p>}
      {value.header && <p>{value.header}</p>}
      <DisplayDetails layout={value.businessLayout} value={[
        ['Document number', type === 'invoice' ? 'SI-SAMPLE-001' : 'CM-SAMPLE-001'], ['Document date', '15 January 2026'],
        ['Registered business', 'Harbor Supply (sample)'], ['Registered address', 'Sample business address'], ['Seller TIN', '000-000-000 (sample)'],
      ]} />
      {type === 'credit-memo' && <p>Original invoice · SI-SAMPLE-001</p>}
      <DisplayDetails layout={value.customerLayout} value={[
        ['Customer', 'Sample Customer'], ['Customer address', 'Sample customer address'], ['Customer TIN', '000-000-001 (sample)'],
      ]} />
      <Table aria-label="Fictional line items" className="workspace-table">
        <thead><tr>{value.columns.map(key => <th key={key} scope="col">{columns[key].label}</th>)}</tr></thead>
        <tbody><tr>{value.columns.map(key => <td key={key}>{columns[key].example}</td>)}</tr></tbody>
      </Table>
      <DisplayDetails layout="split" value={[['Subtotal', '₱900.00'], ['Discount', '₱0.00'], ['Tax', '₱108.00'], ['Total', '₱1,008.00']]} />
      {value.paymentInstructions && <p className="mail-preview-body">{value.paymentInstructions}</p>}
      {value.terms && <p className="mail-preview-body">{value.terms}</p>}
      {value.signatureField && <p>Authorized signature ____________________</p>}
      {value.footer && <p className="mail-preview-body">{value.footer}</p>}
      {value.pageNumbers && <p className="template-page-number">Page 1 of 1</p>}
    </article>
  </Card>
}
