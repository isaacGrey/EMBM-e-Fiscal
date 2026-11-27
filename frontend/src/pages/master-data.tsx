import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { ArrowDown, ArrowUp, Plus } from 'lucide-react'
import { Badge, Button, ErrorState, Field, Input, LoadingState, Select, Table } from '@/components/ui'
import { api, errorMessage, manilaTimestamp } from '@/lib/account-api'
import { masterQueryKey, masterRoot, optionLabel, type MasterDefinition, type MasterDetail, type MasterMenu, type MasterPage } from '@/lib/master-data'
import { MasterRecordPanel, singular } from './master-record'
import { MasterImportPanel } from './master-import'
import { CalculationPreview } from './master-calculation'
import './master-data.css'

export function MasterData({ base, section }: { base: string; section: string }) {
  const navigate = useNavigate()
  const menu = useQuery({ queryKey: [...masterQueryKey(base), 'menu'], queryFn: () => api<MasterMenu>(base), retry: false })
  if (menu.isPending) return <LoadingState label="Loading company master data…" />
  if (menu.isError) return <ErrorState>{errorMessage(menu.error)}</ErrorState>
  const data = menu.data
  const calculation = section === 'calculator'
  const definition = data.definitions.find(item => item.key === section) ?? data.definitions[0]
  if (!definition) return <ErrorState>Master data is unavailable for this role.</ErrorState>
  return <div className="master-stack">
    <div className="master-navigation">
      <Field id="master-area" label="Master data area"><Select id="master-area" value={calculation ? 'calculator' : definition.key}
        onChange={event => navigate(masterRoot + '/' + event.target.value)}>
        <optgroup label="Company records">{data.definitions.map(item => <option key={item.key} value={item.key}>{item.label}</option>)}</optgroup>
        {data.canCalculate && <option value="calculator">Calculation Preview</option>}
      </Select></Field>
    </div>
    {calculation ? data.canCalculate ? <CalculationPreview key={base} base={base} /> : <ErrorState>Calculation preview is unavailable for this role.</ErrorState>
      : <Records key={base + definition.key} base={base} menu={data} definition={definition} />}
  </div>
}

type SortKey = 'code' | 'name' | 'active' | 'updatedAt'

function Records({ base, menu, definition }: { base: string; menu: MasterMenu; definition: MasterDefinition }) {
  const client = useQueryClient()
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('')
  const [filter, setFilter] = useState('')
  const [page, setPage] = useState(1)
  const [sort, setSort] = useState<{ key: SortKey; descending: boolean }>({ key: 'code', descending: false })
  const [selected, select] = useState('')
  const [mode, setMode] = useState<'' | 'view' | 'edit' | 'create' | 'import'>('')
  const [notice, setNotice] = useState('')
  const path = base + '/' + definition.key
  const params = new URLSearchParams({ q: search, sort: sort.key, descending: String(sort.descending), page: String(page) })
  if (status) params.set('active', status)
  if (filter) params.set('filter', filter)
  const list = useQuery({ queryKey: [...masterQueryKey(base), 'list', definition.key, params.toString()],
    queryFn: () => api<MasterPage>(path + '?' + params), retry: false })
  const detail = useQuery({ queryKey: [...masterQueryKey(base), 'record', selected],
    queryFn: () => api<MasterDetail>(path + '/' + selected), enabled: !!selected && (mode === 'view' || mode === 'edit'), retry: false })
  const bulk = definition.key === 'customers' || definition.key === 'items'
  const filterField = definition.fields.find(field => field.type === 'choice')
  function open(next: typeof mode, id = '') { setNotice(''); select(id); setMode(next) }
  function order(key: SortKey) { setSort(prior => ({ key, descending: prior.key === key && !prior.descending })); setPage(1) }
  async function refreshed(message: string) {
    await client.invalidateQueries({ queryKey: masterQueryKey(base) })
    setNotice(message)
  }

  return <div className="master-stack">
    {notice && <p className="master-notice" role="status">{notice}</p>}
    <section className="master-panel" aria-labelledby="master-list-heading">
      <div className="master-toolbar"><div><h2 id="master-list-heading">{definition.label}</h2>
        {list.data && <p>{list.data.total} {list.data.total === 1 ? 'record' : 'records'}</p>}</div>
        <div className="master-actions">
          {definition.canWrite && <Button type="button" onClick={() => open('create')}><Plus size={16} /> New {singular(definition.label)}</Button>}
          {definition.canImport && <Button type="button" variant="light" onClick={() => open('import')}>Import</Button>}
          {bulk && definition.canExport && <>
            <a className="text-link" href={'/api' + path + '/file?format=csv'} download>Export CSV</a>
            <a className="text-link" href={'/api' + path + '/file?format=xlsx'} download>Export Excel</a>
          </>}
        </div>
      </div>
      <div className="master-filters">
        <Field id="master-search" label="Search by code or name"><Input id="master-search" type="search" value={search} maxLength={120}
          onChange={event => { setPage(1); setSearch(event.target.value) }} /></Field>
        <Field id="master-status" label="Record status"><Select id="master-status" value={status} onChange={event => { setPage(1); setStatus(event.target.value) }}>
          <option value="">All statuses</option><option value="true">Active</option><option value="false">Inactive</option>
        </Select></Field>
        {filterField && <Field id="master-filter" label={filterField.label}><Select id="master-filter" value={filter} onChange={event => {
          setPage(1); setFilter(event.target.value)
        }}><option value="">All</option>{filterField.choices?.map(item => <option key={item} value={item}>{optionLabel(item)}</option>)}</Select></Field>}
      </div>
      {list.isPending ? <LoadingState label="Loading records…" /> : list.isError ? <ErrorState>{errorMessage(list.error)}</ErrorState>
        : list.data.items.length ? <>
          <div className="master-table" role="region" aria-label={definition.label + ' table'} tabIndex={0}>
            <Table aria-label={definition.label}><thead><tr>
              {([['code', 'Code'], ['name', 'Name'], ['active', 'Status'], ['updatedAt', 'Updated']] as [SortKey, string][]).map(([key, title]) =>
                <th key={key} scope="col" aria-sort={sort.key === key ? sort.descending ? 'descending' : 'ascending' : 'none'}>
                  <button type="button" className="master-sort" onClick={() => order(key)}>{title}
                    {sort.key === key && (sort.descending ? <ArrowDown size={14} /> : <ArrowUp size={14} />)}</button></th>)}
              {definition.effective && <th scope="col">Rule status</th>}
            </tr></thead><tbody>{list.data.items.map(row => <tr key={row.id}>
              <td>{row.code}</td><td><button className="text-link" type="button" onClick={() => open('view', row.id)}>{row.name}</button></td>
              <td><Badge tone={row.active ? 'success' : 'neutral'}>{row.active ? 'Active' : 'Inactive'}</Badge></td>
              <td>{manilaTimestamp.format(new Date(row.updatedAt))}</td>
              {definition.effective && <td><Badge tone={row.verificationStatus === 'AuthorizedForPreview' || row.verificationStatus === 'Commercial' ? 'success' : 'warning'}>
                {optionLabel(row.verificationStatus ?? 'Unverified')}</Badge></td>}
            </tr>)}</tbody></Table>
          </div>
          {list.data.total > list.data.pageSize && <div className="master-actions" aria-label="Record pages">
            <Button type="button" variant="light" disabled={page === 1} onClick={() => setPage(page - 1)}>Previous</Button>
            <span>Page {page} of {Math.ceil(list.data.total / list.data.pageSize)}</span>
            <Button type="button" variant="light" disabled={page * list.data.pageSize >= list.data.total} onClick={() => setPage(page + 1)}>Next</Button>
          </div>}
        </> : <p>{search || status || filter ? 'No records match these filters.' : 'There are no records in this company.'}</p>}
    </section>
    {mode === 'import' && <MasterImportPanel path={path} close={() => open('')} saved={async message => { open(''); await refreshed(message) }} />}
    {(mode === 'view' || mode === 'edit') && (detail.isPending ? <LoadingState label="Loading record…" /> : detail.isError ? <ErrorState>{errorMessage(detail.error)}</ErrorState> : null)}
    {(mode === 'create' || ((mode === 'view' || mode === 'edit') && detail.data)) &&
      <MasterRecordPanel key={mode + selected + (detail.data?.record.revision ?? '')} initial={mode === 'create' ? undefined : detail.data?.record}
        history={mode === 'create' ? undefined : detail.data?.history} definition={definition} menu={menu} base={base} editing={mode !== 'view'}
        startEdit={() => setMode('edit')} close={() => open('')} saved={async (record, message) => { open('view', record.id); await refreshed(message) }} />}
  </div>
}

