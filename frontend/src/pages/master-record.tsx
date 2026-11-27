import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Button, ErrorState, Field, Input, Table } from '@/components/ui'
import { api, ApiError, errorMessage, manilaDate, manilaTimestamp } from '@/lib/account-api'
import { asList, asObject, asText, optionLabel, type AuditRow, type MasterDefinition, type MasterField, type MasterFields, type MasterMenu, type MasterRow, type MasterValue } from '@/lib/master-data'
import { EditorField } from './master-fields'

export function MasterRecordPanel({ initial, definition, menu, base, history = [], editing, close, saved, startEdit }: {
  initial?: MasterRow; definition: MasterDefinition; menu: MasterMenu; base: string; history?: AuditRow[]
  editing: boolean; close: () => void; saved: (record: MasterRow, message: string) => Promise<void>; startEdit: () => void
}) {
  const [code, setCode] = useState(initial?.code ?? '')
  const [name, setName] = useState(initial?.name ?? '')
  const [active, setActive] = useState(initial?.active ?? true)
  const [values, setValues] = useState<MasterFields>(initial?.data ?? (definition.fields.some(x => x.key === 'effectiveFrom') ? { effectiveFrom: manilaDate() } : {}))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const disabled = !editing || busy
  const heading = useRef<HTMLHeadingElement>(null)
  useEffect(() => { heading.current?.focus() }, [])
  const custom = menu.customFields.filter(field => asList(field.data.recordTypes).includes(definition.key))
  const path = base + '/' + definition.key
  const change = (key: string, value: MasterValue) => setValues(prior => ({ ...prior, [key]: value }))

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true); setError(''); setErrors({})
    try {
      const data = { ...values }
      if (definition.fields.some(field => field.type === 'custom-values')) {
        const permitted = new Set(custom.map(row => row.code))
        data.custom = Object.fromEntries(Object.entries(asObject(values.custom)).filter(([field]) => permitted.has(field)))
      }
      const result = await api<MasterRow>(path + (initial ? '/' + initial.id : ''), initial ? 'PUT' : 'POST',
        { code, name, active, revision: initial?.revision, data })
      await saved(result, initial ? 'Changes saved.' : 'Record created.')
    } catch (failure) {
      if (failure instanceof ApiError) setErrors(failure.fields)
      setError(errorMessage(failure))
    } finally { setBusy(false) }
  }

  return <section className="master-panel" aria-labelledby="master-record-heading">
    <div className="master-toolbar">
      <div><h2 id="master-record-heading" tabIndex={-1} ref={heading}>{initial ? initial.name : 'New ' + singular(definition.label)}</h2>
        {initial && <p>{initial.code} · Revision {initial.revision}{initial.verificationStatus ? ' · ' + optionLabel(initial.verificationStatus) : ''}</p>}</div>
      <div className="master-actions">
        {!editing && definition.canWrite && <Button type="button" onClick={startEdit}>Edit record</Button>}
        <Button type="button" variant="light" disabled={busy} onClick={close}>{editing ? 'Cancel' : 'Close record'}</Button>
      </div>
    </div>
    <form onSubmit={event => void submit(event)}>
      <fieldset className={editing ? 'master-form-grid' : 'master-form-grid master-readonly'} disabled={disabled}>
        <Field id="master-record-code" label="Code" error={errors.code?.[0]}>
          <Input id="master-record-code" value={code} onChange={e => setCode(e.target.value)} required readOnly={!!initial} maxLength={40} />
        </Field>
        <Field id="master-record-name" label="Name" error={errors.name?.[0]}>
          <Input id="master-record-name" value={name} onChange={e => setName(e.target.value)} required maxLength={160} />
        </Field>
        {definition.fields.map(field => <EditorField key={field.key} field={field} id={'master-record-' + field.key}
          value={values[field.key]} change={value => change(field.key, value)} base={base} menu={menu} kind={definition.key} disabled={disabled}
          error={errors[field.key]?.[0]} />)}
        {!!custom.length && <fieldset className="master-nested master-wide">
          <legend>Custom fields</legend>
          <div className="master-form-grid">{custom.map(row => {
            const field = customField(row)
            const current = asObject(values.custom)[row.code]
            const fallback = asText(row.data.default)
            const value = current ?? (field.type === 'boolean' && fallback ? fallback.toLowerCase() === 'true' : fallback)
            return <EditorField key={row.code} field={field} id={'master-custom-' + row.code} base={base} menu={menu}
              kind={definition.key} disabled={disabled} value={value} error={errors['custom.' + row.code]?.[0]}
              change={next => change('custom', { ...asObject(values.custom), [row.code]: next })} />
          })}</div>
        </fieldset>}
        <label className="master-check master-wide">
          <input type="checkbox" checked={active} onChange={e => setActive(e.target.checked)} /> Active record
        </label>
      </fieldset>
      {error && <ErrorState>{error}</ErrorState>}
      {editing && <Button type="submit" disabled={busy}>{busy ? 'Saving…' : initial ? 'Save changes' : 'Create record'}</Button>}
    </form>
    {history.length > 0 && <details className="master-history">
      <summary>Audit and revision history</summary>
      <div className="master-table" role="region" aria-label="Master-data revision history" tabIndex={0}>
        <Table><thead><tr>{['Revision', 'Change', 'Date and time', 'Effective dates', 'Actor'].map(title => <th scope="col" key={title}>{title}</th>)}</tr></thead>
          <tbody>{history.map(row => <tr key={row.revision}><td>{row.revision}</td><td>{row.action}{row.active ? '' : ' · Inactive'}</td>
            <td>{manilaTimestamp.format(new Date(row.at))}</td><td>{[row.effectiveFrom, row.effectiveThrough].filter(Boolean).join(' to ') || '—'}</td><td>{row.actorId}</td></tr>)}</tbody>
        </Table>
      </div>
      <div className="master-stack">{history.slice(0, 12).map(row => <details key={row.revision}><summary>Revision {row.revision} values</summary>
        <dl className="workspace-definitions">{definition.fields.filter(field => row.data[field.key] != null).map(field => <div key={field.key}>
          <dt>{field.label}</dt><dd>{display(row.data[field.key])}</dd></div>)}</dl>
      </details>)}</div>
    </details>}
  </section>
}

export function singular(label: string) {
  if (label === 'Units of Measure') return 'Unit of Measure'
  return label === 'Items and Services' ? 'Item or Service' : label.endsWith('ies') ? label.slice(0, -3) + 'y' : label.replace(/s$/, '')
}
function display(value: MasterValue | undefined) {
  if (typeof value === 'boolean') return value ? 'Yes' : 'No'
  if (value && typeof value === 'object') return JSON.stringify(value)
  return asText(value)
}
function customField(row: MasterRow): MasterField {
  const type = asText(row.data.valueType)
  return { key: row.code, label: row.name, type: type === 'Dropdown' ? 'choice' : type.toLowerCase(),
    required: row.data.required === true, choices: asList(row.data.values).map(asText), reference: asText(row.data.referenceType) || null,
    maxLength: Number(asText(row.data.maximumLength) || 240) }
}

