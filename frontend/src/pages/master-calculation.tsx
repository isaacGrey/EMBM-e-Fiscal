import { useState, type FormEvent } from 'react'
import { Plus } from 'lucide-react'
import { Button, ErrorState, Field, Input, Select, Table } from '@/components/ui'
import { api, errorMessage, manilaDate } from '@/lib/account-api'
import { optionLabel } from '@/lib/master-data'
import { Reference } from './master-fields'

type Eligibility = { category: string; quantity: string; identityChecked: boolean; exclusiveUse: boolean; itemCovered: boolean; supportingEvidenceChecked: boolean; competingPromotion: boolean }
type Entry = { key: number; itemId: string; unitId: string; quantity: string; unitPrice: string; taxGroupId: string; discountGroupIds: string[] | null; eligibility: Eligibility }
type Quote = { name: string; unitPrice: string; baseFactor: string; currency: string }
type Result = {
  currency: { code: string; exchangeRate: string; precision: number; source: string }; dueDate: string; postingDirection: number
  totals: Record<string, string>
  lines: { item: { name: string }; quantity: string; unitPrice: string; tax: { name: string; version: number }; vat: string; total: string
    discounts: { amount: string; rule: { name: string; version: number } }[] }[]
}
const blank = (key: number): Entry => ({ key, itemId: '', unitId: '', quantity: '1', unitPrice: '', taxGroupId: '',
  discountGroupIds: null, eligibility: { category: '', quantity: '', identityChecked: false, exclusiveUse: false, itemCovered: false,
    supportingEvidenceChecked: false, competingPromotion: false } })
const totalsLabels: Record<string, string> = {
  grossLines: 'Gross line amounts', commercialDiscount: 'Commercial discounts', statutoryDiscount: 'Statutory discounts',
  vatableSales: 'VATable sales', vatExemptSales: 'VAT-exempt sales', zeroRatedSales: 'Zero-rated sales', nonVatSales: 'Non-VAT sales',
  vat: 'Output VAT', grossDocumentTotal: 'Gross document total', incomeWithholding: 'Creditable withholding tax', vatWithholding: 'Withholding VAT',
  netAmountDue: 'Net amount due', roundingAdjustment: 'Rounding adjustment',
}

export function CalculationPreview({ base }: { base: string }) {
  const [date, setDate] = useState(manilaDate)
  const [purpose, setPurpose] = useState('SalesInvoice')
  const [customer, setCustomer] = useState('')
  const [list, setList] = useState('')
  const [currency, setCurrency] = useState('')
  const [entries, setEntries] = useState<Entry[]>([blank(1)])
  const [discounts, setDiscounts] = useState<string[]>([])
  const [result, setResult] = useState<Result>()
  const [quote, setQuote] = useState<{ key: number; data: Quote }>()
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  function clear() { setResult(undefined); setQuote(undefined); setError('') }
  function change(key: number, values: Partial<Entry>) {
    clear()
    setEntries(rows => rows.map(row => row.key === key ? { ...row, ...values } : row))
  }
  const context = () => ({ date, currency: currency || null, customerId: customer || null, priceListId: list || null })

  async function preview(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    clear(); setBusy(true)
    try {
      const lines = entries.map(row => ({ itemId: row.itemId, unitId: row.unitId || null, quantity: row.quantity,
        unitPrice: row.unitPrice || null, taxGroupId: row.taxGroupId || null, discountGroupIds: row.discountGroupIds,
        eligibility: row.eligibility.category ? { ...row.eligibility, quantity: row.eligibility.quantity || null } : null }))
      setResult(await api<Result>(base + '/calculate', 'POST', { ...context(), purpose, lines, documentDiscountIds: discounts }))
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  async function price(line: Entry) {
    setError(''); setQuote(undefined); setBusy(true)
    try {
      const data = await api<Quote>(base + '/resolve-price', 'POST', { ...context(), itemId: line.itemId, quantity: line.quantity, unitId: line.unitId || null })
      setQuote({ key: line.key, data })
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }

  return <>
    <section className="master-panel" aria-labelledby="master-calculator-heading">
      <h2 id="master-calculator-heading">Calculation Preview</h2>
      <p>Check effective prices, discounts and tax before entering a financial document. Preview uses zero Save &amp; Send Credits.</p>
      <form onSubmit={event => void preview(event)}>
        <fieldset className="master-form-grid" disabled={busy}>
          <Field id="calculation-purpose" label="Document type"><Select id="calculation-purpose" value={purpose} onChange={e => { clear(); setPurpose(e.target.value) }}>
            {['SalesInvoice', 'CreditMemo', 'SalesOrder'].map(item => <option key={item} value={item}>{optionLabel(item)}</option>)}
          </Select></Field>
          <Field id="calculation-date" label="Transaction date"><Input id="calculation-date" type="date" value={date} required onChange={e => { clear(); setDate(e.target.value) }} /></Field>
          <Reference base={base} kind="customers" id="calculation-customer" label="Customer" value={customer} onChange={value => { clear(); setCustomer(value) }}
            emptyLabel="No customer selected" />
          <Reference base={base} kind="price-lists" id="calculation-list" label="Price list" value={list} onChange={value => { clear(); setList(value) }}
            emptyLabel="Resolve using company defaults" />
          <Field id="calculation-currency" label="Currency code"><Input id="calculation-currency" value={currency} maxLength={3}
            placeholder="Customer default or PHP" onChange={event => { clear(); setCurrency(event.target.value) }} /></Field>
        </fieldset>
        <fieldset className="master-stack master-fieldset" disabled={busy}>
          {entries.map((line, index) => <fieldset key={line.key} className="master-nested">
            <legend>Line {index + 1}</legend>
            <div className="master-form-grid">
              <Reference base={base} kind="items" id={'calculation-item-' + line.key} label={'Item for line ' + (index + 1)}
                value={line.itemId} onChange={value => change(line.key, { itemId: value, unitId: '' })} required />
              <Reference base={base} kind="units" id={'calculation-unit-' + line.key} label={'Unit for line ' + (index + 1)}
                value={line.unitId} onChange={value => change(line.key, { unitId: value })} emptyLabel="Item base unit" />
              <Field id={'calculation-quantity-' + line.key} label={'Quantity for line ' + (index + 1)}>
                <Input id={'calculation-quantity-' + line.key} inputMode="decimal" value={line.quantity} required onChange={e => change(line.key, { quantity: e.target.value })} />
              </Field>
              <Field id={'calculation-price-' + line.key} label={'Unit price override for line ' + (index + 1)}>
                <Input id={'calculation-price-' + line.key} inputMode="decimal" value={line.unitPrice} placeholder="Resolved price"
                  onChange={event => change(line.key, { unitPrice: event.target.value })} />
              </Field>
              <Reference base={base} kind="tax-groups" id={'calculation-tax-' + line.key} label={'Tax group for line ' + (index + 1)}
                value={line.taxGroupId} onChange={value => change(line.key, { taxGroupId: value })} emptyLabel="Use applicable default" />
            </div>
            <details className="master-history"><summary>Line discounts and eligibility</summary>
              <label className="master-check"><input type="checkbox" checked={line.discountGroupIds === null}
                onChange={event => change(line.key, { discountGroupIds: event.target.checked ? null : [] })} /> Use the default line discount</label>
              {line.discountGroupIds !== null && <DiscountPicker base={base} id={'line-discounts-' + line.key} label="Line discount"
                discounts={line.discountGroupIds} change={ids => change(line.key, { discountGroupIds: ids })} />}
              <Proof line={line} change={eligibility => change(line.key, { eligibility })} />
            </details>
            <div className="master-actions">
              <Button type="button" variant="light" disabled={!line.itemId || !line.quantity || !date} onClick={() => void price(line)}>Resolve item price</Button>
              {entries.length > 1 && <Button type="button" variant="light" onClick={() => { clear(); setEntries(rows => rows.filter(x => x.key !== line.key)) }}>Remove line {index + 1}</Button>}
            </div>
            {quote?.key === line.key && <p role="status">{quote.data.name} · {quote.data.currency} {quote.data.unitPrice} per unit · conversion factor {quote.data.baseFactor}</p>}
          </fieldset>)}
          <div className="master-actions"><Button type="button" variant="light" disabled={entries.length >= 100}
            onClick={() => { clear(); setEntries(rows => [...rows, blank(Math.max(...rows.map(x => x.key)) + 1)]) }}><Plus size={16} /> Add line</Button></div>
          <DiscountPicker base={base} id="document-discounts" label="Document discount" discounts={discounts}
            change={ids => { clear(); setDiscounts(ids) }} />
        </fieldset>
        {error && <ErrorState>{error}</ErrorState>}
        <Button type="submit" disabled={busy}>{busy ? 'Calculating…' : 'Calculate preview'}</Button>
      </form>
    </section>
    {result && <section className="master-panel" aria-labelledby="master-results-heading" aria-live="polite">
      <h2 id="master-results-heading">Calculated totals</h2>
      <p>{result.currency.code} · Due {result.dueDate}{result.postingDirection === -1 ? ' · Credit Memo reversal' : ''}</p>
      {result.currency.code !== 'PHP' && <p>PHP exchange rate {result.currency.exchangeRate} · {result.currency.source}</p>}
      <dl className="workspace-definitions">{Object.entries(totalsLabels).map(([key, label]) =>
        <div key={key}><dt>{label}</dt><dd>{displayMoney(result.totals[key], result.currency.precision)}</dd></div>)}</dl>
      <div className="master-table" role="region" aria-label="Calculation line breakdown" tabIndex={0}>
        <Table><thead><tr>{['Item', 'Quantity', 'Unit price', 'Tax rule', 'Discounts', 'VAT', 'Total'].map(title => <th scope="col" key={title}>{title}</th>)}</tr></thead>
          <tbody>{result.lines.map((row, index) => <tr key={index}><td>{row.item.name}</td><td>{row.quantity}</td><td>{row.unitPrice}</td>
            <td>{row.tax.name} · v{row.tax.version}</td><td>{row.discounts.map(x => x.rule.name + ' v' + x.rule.version + ' · ' + x.amount).join(', ') || '—'}</td>
            <td>{displayMoney(row.vat, result.currency.precision)}</td><td>{displayMoney(row.total, result.currency.precision)}</td></tr>)}</tbody>
        </Table>
      </div>
    </section>}
  </>
}

function displayMoney(value: string, precision: number) {
  const [whole, fractional = ''] = value.split('.')
  const remainder = fractional.padEnd(precision, '0')
  return remainder ? whole + '.' + remainder : whole
}

function DiscountPicker({ base, id, label, discounts, change }: {
  base: string; id: string; label: string; discounts: string[]; change: (ids: string[]) => void
}) {
  return <div className="master-stack">
    {discounts.map((value, index) => <div className="master-choice-row" key={index}>
      <Reference base={base} kind="discount-groups" id={id + '-' + index} label={label + ' ' + (index + 1)} required
        value={value} onChange={next => change(discounts.map((item, i) => i === index ? next : item))} />
      <Button type="button" variant="light" onClick={() => change(discounts.filter((_, i) => index !== i))}>Remove {label.toLowerCase()} {index + 1}</Button>
    </div>)}
    <div><Button type="button" variant="light" disabled={discounts.length >= 20} onClick={() => change([...discounts, ''])}>Add {label.toLowerCase()}</Button></div>
  </div>
}
function Proof({ line, change }: { line: Entry; change: (value: Eligibility) => void }) {
  const record = line.eligibility
  const id = 'eligibility-' + line.key
  const flags = [
    ['identityChecked', 'Checked eligible individual’s proof of identity'], ['exclusiveUse', 'Confirmed exclusive personal use'],
    ['itemCovered', 'Confirmed that the goods are covered'], ['supportingEvidenceChecked', 'Checked the required supporting evidence'],
    ['competingPromotion', 'A competing promotional discount is available'],
  ] as const
  return <div className="master-stack">
    <Field id={id} label="Eligibility scenario"><Select id={id} value={record.category} onChange={event => change({ ...record, category: event.target.value })}>
      <option value="">Select when applicable</option>
      {[['SeniorCitizen', 'Senior citizen qualifying medicine'], ['PWD', 'PWD qualifying medicine'], ['CoveredPublication', 'Qualifying publication'],
        ['DirectExport', 'Direct export of goods']].map(([value, name]) => <option key={value} value={value}>{name}</option>)}
    </Select></Field>
    {record.category && <>
      {['SeniorCitizen', 'PWD'].includes(record.category) && <Field id={id + '-quantity'} label="Eligible quantity">
        <Input id={id + '-quantity'} inputMode="decimal" value={record.quantity} placeholder="Entire line"
          onChange={event => change({ ...record, quantity: event.target.value })} />
      </Field>}
      {flags.filter(([key]) => ['SeniorCitizen', 'PWD'].includes(record.category) || ['itemCovered', 'supportingEvidenceChecked'].includes(key))
        .map(([key, label]) => <label className="master-check" key={key}><input type="checkbox" checked={record[key]}
          onChange={event => change({ ...record, [key]: event.target.checked })} /> {label}</label>)}
    </>}
  </div>
}

