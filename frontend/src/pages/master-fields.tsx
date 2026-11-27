import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button, ErrorState, Field, Input, Select } from '@/components/ui'
import { api, errorMessage } from '@/lib/account-api'
import { asList, asObject, asText, masterQueryKey, optionLabel, type LookupRow, type MasterField, type MasterMenu, type MasterValue } from '@/lib/master-data'

export function Reference({ base, kind, id, label, value, onChange, required = false, disabled = false, emptyLabel = 'Select a record', error }: {
  base: string; kind: string; id: string; label: string; value: string; onChange: (value: string) => void
  required?: boolean; disabled?: boolean; emptyLabel?: string; error?: string
}) {
  const [search, setSearch] = useState('')
  const params = new URLSearchParams()
  if (search) params.set('q', search)
  if (value) params.set('include', value)
  const suffix = params.toString()
  const query = useQuery({ queryKey: [...masterQueryKey(base), 'lookup', kind, suffix],
    queryFn: () => api<LookupRow[]>(base + '/' + kind + '/lookup?' + suffix), retry: false })
  return <div className="master-reference">
    <Field id={id} label={label} error={error}>
      <Select id={id} value={value} required={required} disabled={disabled || query.isPending} onChange={event => onChange(event.target.value)}>
        <option value="">{query.isPending ? 'Loading records…' : emptyLabel}</option>
        {query.data?.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}{item.active ? '' : ' (inactive)'}</option>)}
      </Select>
    </Field>
    {!disabled && (query.data?.length === 50 || search) && <Input type="search" aria-label={'Search ' + label.toLowerCase()} value={search}
      placeholder="Search by code or name" maxLength={120} onChange={event => setSearch(event.target.value)} />}
    {query.isError && <ErrorState>{errorMessage(query.error)}</ErrorState>}
  </div>
}

type Props = {
  field: MasterField; id: string; value: MasterValue | undefined; change: (value: MasterValue) => void
  base: string; menu: MasterMenu; kind: string; disabled?: boolean; error?: string
}

export function EditorField({ field, id, value, change, base, menu, kind, disabled, error }: Props) {
  const text = asText(value)
  const common = { id, required: field.required, 'aria-invalid': !!error }
  const choice = (options: { value: string; label: string }[]) => <Select {...common} value={text} onChange={event => change(event.target.value)}>
    <option value="">Select an option</option>{options.map(x => <option key={x.value} value={x.value}>{x.label}</option>)}
  </Select>
  if (field.reference) return <Reference id={id} label={field.label} kind={field.reference} base={base} value={text} onChange={change}
    required={field.required} disabled={disabled} error={error} />
  if (field.type === 'boolean') return <label className="master-check"><input id={id} type="checkbox" checked={value === true}
    onChange={event => change(event.target.checked)} /> {field.label}</label>
  if (field.type === 'record-types') return <fieldset className="master-nested">
    <legend>{field.label}</legend>
    {menu.recordTypes.map(type => <label className="master-check" key={type}><input type="checkbox" checked={asList(value).includes(type)}
      onChange={event => change(event.target.checked ? [...asList(value), type] : asList(value).filter(x => x !== type))} /> {optionLabel(type)}</label>)}
    {error && <ErrorState>{error}</ErrorState>}
  </fieldset>
  if (field.type === 'components' || field.type === 'external') return <RepeatingField field={field} id={id} value={value}
    change={change} kind={kind} error={error} disabled={disabled} />
  if (field.type === 'custom-values') return null
  const reference = menu.regulatoryReferences.find(x => x.id === text)
  return <Field id={id} label={field.label} error={error}>
    {field.type === 'choice' ? choice((field.choices ?? []).map(value => ({ value, label: optionLabel(value) })))
      : field.type === 'rdo' ? choice(menu.rdos.map(row => ({ value: row.code, label: row.code + ' · ' + row.name })))
      : field.type === 'authority' ? <>
        {choice(menu.regulatoryReferences.map(row => ({ value: row.id, label: row.label + (row.verificationStatus === 'ReferenceOnly' ? ' (reference only)' : '') })))}
        {reference && <small className="master-hint">{reference.restrictions} {reference.verificationStatus !== 'ReferenceOnly' && 'Preview coverage begins ' + reference.supportedFrom + '. '}
          <a href={reference.source} target="_blank" rel="noreferrer" className="text-link">
          {reference.authority} source</a></small>}
      </>
      : field.type === 'multiline' ? <textarea {...common} maxLength={field.maxLength} value={text} rows={3} onChange={event => change(event.target.value)} />
      : field.type === 'emails' || field.type === 'list' ? <ListField id={id} value={value} change={change} />
      : <Input {...common} type={field.type === 'date' || field.type === 'email' ? field.type : 'text'} maxLength={field.maxLength}
        inputMode={field.type === 'decimal' ? 'decimal' : field.type === 'integer' ? 'numeric' : undefined} value={text}
        onChange={event => change(event.target.value)} />}
  </Field>
}

function ListField({ id, value, change }: { id: string; value: MasterValue | undefined; change: (value: MasterValue) => void }) {
  const [entry, setEntry] = useState(asList(value).join('; '))
  return <><Input id={id} value={entry} onChange={event => {
    setEntry(event.target.value)
    change(event.target.value.split(';').map(x => x.trim()).filter(Boolean))
  }} /><small className="master-hint">Separate entries with semicolons.</small></>
}

function RepeatingField({ field, id, value, change, kind, error, disabled }: Pick<Props, 'field' | 'id' | 'value' | 'change' | 'kind' | 'error' | 'disabled'>) {
  const rows = asList(value)
  const taxes = field.type === 'components'
  const keys = taxes
    ? [['kind', 'Component type'], ['basis', 'Calculation base'], ['rate', 'Rate (%)'], ['reference', 'Source reference']]
    : [['connector', 'Connector'], ['externalAccount', 'External account ID'], ['externalId', 'External record ID'],
      ['externalCode', 'External visible code'], ['sourceAt', 'Source timestamp (ISO 8601)']]
  const setRow = (index: number, key: string, text: string) => change(rows.map((row, position) => position === index
    ? { ...asObject(row), [key]: text || null } : row))
  return <fieldset className="master-nested master-wide">
    <legend>{field.label}</legend>
    {rows.map((row, index) => <div className="master-repeat" key={index}>
      <div className="master-form-grid">{keys.map(([key, label]) => {
        const control = id + '-' + key + '-' + index
        const text = asText(asObject(row)[key])
        const choices = key === 'kind' ? ['VAT', 'CreditableWithholding', 'WithholdingVAT', 'Other'] : key === 'basis' ? ['NetSales', 'GrossTotal'] : null
        return <Field key={key} id={control} label={label}>
          {choices ? <Select id={control} value={text} required onChange={event => setRow(index, key, event.target.value)}>
            <option value="">Select an option</option>{choices.map(item => <option key={item} value={item}>{optionLabel(item)}</option>)}
          </Select> : <Input id={control} value={text} required={['rate', 'connector', 'externalAccount', 'externalId'].includes(key)}
            inputMode={key === 'rate' ? 'decimal' : undefined} onChange={event => setRow(index, key, event.target.value)} />}
        </Field>
      })}</div>
      {!disabled && <Button type="button" variant="light" onClick={() => change(rows.filter((_, position) => position !== index))}>
        Remove {taxes ? 'component' : 'external reference'} {index + 1}</Button>}
    </div>)}
    {error && <ErrorState>{error}</ErrorState>}
    {!disabled && <Button type="button" variant="light" disabled={rows.length >= (taxes ? 4 : 20)} onClick={() => change([...rows,
      taxes ? { kind: '', basis: 'NetSales', rate: '', reference: null } : { connector: '', externalAccount: '', recordType: kind, externalId: '', externalCode: null, sourceAt: null }
    ])}>Add {taxes ? 'tax component' : 'external reference'}</Button>}
  </fieldset>
}

