import { Button, Field, Input, Select } from '@/components/ui'
import { asList, asText, optionLabel, type MasterFields, type MasterMenu, type MasterValue } from '@/lib/master-data'
import type { DocumentType, SourceLine } from '@/lib/document-api'
import { EditorField, Reference } from './master-fields'

export type Eligibility = { category: string; quantity: string; identityChecked: boolean; exclusiveUse: boolean; itemCovered: boolean; supportingEvidenceChecked: boolean; competingPromotion: boolean }
export type LineDraft = {
  key: number; itemId: string; unitId: string; quantity: string; unitPrice: string; taxGroupId: string; discountGroupIds: string[] | null
  sourceLineId: string; externalLineKey: string; adjustmentAmount: string; description: string
  projectId: string; departmentId: string; locationId: string; accountId: string; custom: MasterFields; eligibility: Eligibility
}
export const blankLine = (key: number): LineDraft => ({ key, itemId: '', unitId: '', quantity: '1', unitPrice: '', taxGroupId: '',
  discountGroupIds: null, sourceLineId: '', externalLineKey: '', adjustmentAmount: '', description: '', projectId: '', departmentId: '',
  locationId: '', accountId: '', custom: {}, eligibility: { category: '', quantity: '', identityChecked: false, exclusiveUse: false,
    itemCovered: false, supportingEvidenceChecked: false, competingPromotion: false } })

export function documentCustomKind(type: DocumentType, line: boolean) {
  if (type === 'SalesOrder') return 'sales-order'
  return (type === 'SalesInvoice' ? 'sales-invoice' : 'credit-memo') + (line ? '-line' : '-header')
}

export function DocumentCustomFields({ id, type, line, values, change, master, menu }: {
  id: string; type: DocumentType; line: boolean; values: MasterFields; change: (values: MasterFields) => void; master: string; menu?: MasterMenu
}) {
  const kind = documentCustomKind(type, line)
  const definitions = menu?.customFields.filter(field => asList(field.data.recordTypes).includes(kind)) ?? []
  if (!menu || !definitions.length) return null
  return <details><summary>{line ? 'Line custom fields' : 'Header custom fields'}</summary>
    <div className="doc-grid">{definitions.map(row => {
      const type = asText(row.data.valueType)
      const field = { key: row.code, label: row.name, type: type === 'Dropdown' ? 'choice' : type.toLowerCase(),
        required: row.data.required === true, choices: asList(row.data.values).map(asText), reference: asText(row.data.referenceType) || null,
        maxLength: Number(asText(row.data.maximumLength) || 240) }
      const fallback = asText(row.data.default)
      const value = values[row.code] ?? (type === 'Boolean' && fallback ? fallback.toLowerCase() === 'true' : fallback)
      return <EditorField key={row.code} field={field} id={id + '-' + row.code} value={value} base={master}
        menu={menu} kind={kind} change={(x: MasterValue) => change({ ...values, [row.code]: x })} />
    })}</div>
  </details>
}

export function DiscountSelections({ id, label, base, values, change }: {
  id: string; label: string; base: string; values: string[]; change: (list: string[]) => void
}) {
  return <div className="doc-stack">
    {values.map((value, index) => <div className="doc-grid" key={index}>
      <Reference base={base} kind="discount-groups" id={id + '-' + index} label={label + ' ' + (index + 1)} value={value}
        onChange={next => change(values.map((x, n) => n === index ? next : x))} required />
      <Button type="button" variant="light" onClick={() => change(values.filter((_, n) => n !== index))}>Remove discount {index + 1}</Button>
    </div>)}
    <div><Button type="button" variant="light" disabled={values.length >= 20}
      onClick={() => change([...values, ''])}>Add {label.toLowerCase()}</Button></div>
  </div>
}

export function DocumentLineFields({ line, index, type, master, menu, source, linked, amountOnly, change, remove }: {
  line: LineDraft; index: number; type: DocumentType; master: string; menu?: MasterMenu; source?: SourceLine
  linked: boolean; amountOnly: boolean; change: (value: Partial<LineDraft>) => void; remove?: () => void
}) {
  const id = 'document-line-' + line.key
  const credit = type === 'CreditMemo'
  return <fieldset className="doc-line">
    <legend>Line {index + 1}</legend>
    {linked && source && <p className="doc-note">{source.itemCode} · {source.description} · {source.remainingQuantity} remaining of {source.quantity}
      {credit && ' · ' + source.remainingAmount + ' remaining amount · ' + source.usedAmount + ' credited'}</p>}
    <div className="doc-grid">
      {(!linked || !source && !credit) && <Reference base={master} kind="items" id={id + '-item'} label={'Item for line ' + (index + 1)}
        value={line.itemId} onChange={itemId => change({ itemId, unitId: '' })} required disabled={linked} />}
      {!credit && <Reference base={master} kind="units" id={id + '-unit'} label={'Unit for line ' + (index + 1)}
        value={line.unitId} onChange={unitId => change({ unitId })} emptyLabel="Item base unit" />}
      {amountOnly ? <Field id={id + '-amount'} label={'Credit amount for line ' + (index + 1)}>
        <Input id={id + '-amount'} value={line.adjustmentAmount} onChange={event => change({ adjustmentAmount: event.target.value })}
          inputMode="decimal" placeholder={source?.remainingAmount ?? ''} required />
      </Field> : <Field id={id + '-qty'} label={'Quantity for line ' + (index + 1)}>
        <Input id={id + '-qty'} value={line.quantity} onChange={event => change({ quantity: event.target.value })} inputMode="decimal" required />
      </Field>}
      {!credit && <>
        <Field id={id + '-price'} label={'Unit price override for line ' + (index + 1)}>
          <Input id={id + '-price'} value={line.unitPrice} onChange={event => change({ unitPrice: event.target.value })} inputMode="decimal" placeholder="Resolved price" />
        </Field>
        <Reference base={master} kind="tax-groups" id={id + '-tax'} label={'Tax group for line ' + (index + 1)}
          value={line.taxGroupId} onChange={taxGroupId => change({ taxGroupId })} emptyLabel="Use applicable default" />
        <Field id={id + '-description'} label={'Description for line ' + (index + 1)}>
          <Input id={id + '-description'} value={line.description} onChange={event => change({ description: event.target.value })} maxLength={600} />
        </Field>
      </>}
    </div>
    {!credit && <details><summary>Discounts and statutory eligibility</summary>
      <div className="doc-stack">
        <label className="doc-check"><input type="checkbox" checked={line.discountGroupIds === null}
          onChange={e => change({ discountGroupIds: e.target.checked ? null : [] })} /> Use the default line discount</label>
        {line.discountGroupIds && <DiscountSelections base={master} id={id + '-discount'} label="Line discount" values={line.discountGroupIds}
          change={discountGroupIds => change({ discountGroupIds })} />}
        <Proof id={id} proof={line.eligibility} change={eligibility => change({ eligibility })} />
      </div>
    </details>}
    <details><summary>Accounting dimensions</summary>
      <div className="doc-grid">{(['projectId', 'departmentId', 'locationId', 'accountId'] as const).map((key, index) =>
        <Reference key={key} base={master} kind="dimensions" id={id + '-' + key}
          label={['Project', 'Department', 'Location', 'Accounting account'][index]} value={line[key]} onChange={value => change({ [key]: value })} />)}</div>
    </details>
    <DocumentCustomFields id={id + '-custom'} type={type} line values={line.custom} change={custom => change({ custom })} master={master} menu={menu} />
    {remove && <div><Button type="button" variant="light" onClick={remove}>Remove line {index + 1}</Button></div>}
  </fieldset>
}

function Proof({ id, proof, change }: { id: string; proof: Eligibility; change: (value: Eligibility) => void }) {
  const key = id + '-eligibility'
  const choices = ['SeniorCitizen', 'PWD', 'CoveredPublication', 'DirectExport']
  const special = ['SeniorCitizen', 'PWD'].includes(proof.category)
  const flags = [
    ['identityChecked', 'Checked customer eligibility and identity'],
    ['exclusiveUse', 'Confirmed personal and exclusive use'],
    ['itemCovered', 'Confirmed the item is covered'],
    ['supportingEvidenceChecked', 'Checked supporting evidence'],
    ['competingPromotion', 'A competing promotion is available'],
  ] as const
  return <div className="doc-stack">
    <Field id={key} label="Eligibility scenario"><Select id={key} value={proof.category} onChange={e => change({ ...proof, category: e.target.value })}>
      <option value="">Select if applicable</option>{choices.map(x => <option key={x} value={x}>{optionLabel(x)}</option>)}
    </Select></Field>
    {proof.category && <>
      {special && <Field id={key + '-quantity'} label="Eligible quantity"><Input id={key + '-quantity'} value={proof.quantity} inputMode="decimal"
        placeholder="Entire line" onChange={e => change({ ...proof, quantity: e.target.value })} /></Field>}
      {flags.filter(([flag]) => special || ['itemCovered', 'supportingEvidenceChecked'].includes(flag)).map(([flag, label]) =>
        <label className="doc-check" key={flag}><input type="checkbox" checked={proof[flag]} onChange={e => change({ ...proof, [flag]: e.target.checked })} />{label}</label>)}
    </>}
  </div>
}

