import { useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useLocation, useNavigate } from 'react-router-dom'
import { Badge, Button, EmptyState, ErrorState, Field, Input, LoadingState, Select } from '@/components/ui'
import { api, apiBlob, errorMessage, manilaDate, manilaTimestamp, sessionQuery } from '@/lib/account-api'
import { ScrollTable } from './connect'
import { AccountShell } from './identity'
import { birAvailability, PortalLayout } from './portals'

const categories = {
  documents: 'Committed documents',
  'tax-summary': 'Tax summary',
  reconciliation: 'Reconciliation',
  reporting: 'Reporting history',
  audit: 'Fiscal audit history',
}
type Category = keyof typeof categories
type Grant = { id: string; scope: 'Company' | 'Rdo' | 'Nationwide'; subscriberId?: string; companyId?: string;
  branchId?: string; rdoCode?: string; categories: Category[]; canExport: boolean; expiresAt: string }
type RecordPage = { items: Record<string, string | number | null>[]; hasMore: boolean }
type Search = { path: string; category: Category; page: number; canExport: boolean }
const uuid = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'

export function BirWorkspace() {
  const availability = useQuery(birAvailability)
  const session = useQuery({ ...sessionQuery, enabled: availability.data?.enabled === true })
  const { state } = useLocation()
  const initialRdo = typeof state?.rdo === 'string' && /^[0-9]{3}[A-Z]?$/.test(state.rdo) ? state.rdo : ''
  if (availability.isPending || availability.data?.enabled && session.isPending) return <LoadingState />
  if (!availability.data?.enabled || session.data?.kind !== 'Regulator') return <PortalLayout role="bir" />
  return <RegulatorRecords initialRdo={initialRdo} />
}

function RegulatorRecords({ initialRdo }: { initialRdo: string }) {
  const cache = useQueryClient()
  const navigate = useNavigate()
  const grants = useQuery({ queryKey: ['regulator-grants'], queryFn: () => api<{ items: Grant[] }>('/bir/grants'), retry: false })
  const [grantId, setGrantId] = useState('')
  const [search, setSearch] = useState<Search>()
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const grant = grants.data?.items.find(item => item.id === grantId) ?? grants.data?.items[0]
  const results = useQuery({
    queryKey: ['regulator-records', search],
    queryFn: () => api<RecordPage>(search!.path + '&page=' + search!.page),
    enabled: !!search,
    retry: false,
    staleTime: 0,
    gcTime: 0,
  })

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!grant) return
    const form = new FormData(event.currentTarget)
    const category = form.get('category') as Category
    const params = new URLSearchParams({ grantId: grant.id })
    for (const field of ['subscriberId', 'companyId', 'branchId', 'from', 'through', 'rdo']) {
      const value = String(form.get(field) ?? '').trim()
      if (value) params.set(field, value)
    }
    setError('')
    const path = '/bir/records/' + category + '?' + params
    if (search?.path === path && search.page === 1) void results.refetch()
    else setSearch({ path, category, page: 1, canExport: grant.canExport })
  }

  async function signOut() {
    setBusy(true)
    try {
      await api('/auth/logout', 'POST')
      cache.clear()
      navigate('/bir/sign-in', { replace: true })
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }

  async function download() {
    if (!search) return
    setBusy(true)
    setError('')
    try {
      const file = await apiBlob(search.path + '&page=' + search.page + '&export=true', 'GET')
      const url = URL.createObjectURL(file)
      const link = document.createElement('a')
      link.href = url
      link.download = 'efiscal-regulator-' + search.category + '-page-' + search.page + '.json'
      link.click()
      setTimeout(() => URL.revokeObjectURL(url), 1000)
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }

  return <AccountShell title="BIR Read-Only Portal" description="Synthetic regulator workspace for scoped record review." wide>
    <div className="connect-stack">
      <div className="connect-actions"><Badge>Development fixture</Badge><Button variant="light" disabled={busy} onClick={() => void signOut()}>Sign Out</Button></div>
      {error && <ErrorState>{error}</ErrorState>}
      {grants.isPending ? <LoadingState label="Loading active grants" /> : grants.isError ? <ErrorState>{errorMessage(grants.error)}</ErrorState> :
        !grant ? <EmptyState title="No active grants">A local operator can assign a synthetic read grant.</EmptyState> :
        <form key={grant.id} className="connect-stack" onSubmit={submit}>
          <Field id="regulator-grant" label="Active grant">
            <Select id="regulator-grant" value={grant.id} onChange={event => { setGrantId(event.target.value); setSearch(undefined); setError('') }}>
              {grants.data.items.map(item => <option key={item.id} value={item.id}>
                {item.scope}{item.rdoCode ? ' ' + item.rdoCode : ''} · {item.id.slice(0, 8)} · Expires {manilaTimestamp.format(new Date(item.expiresAt))}
              </option>)}
            </Select>
          </Field>
          <div className="connect-grid">
            <Field id="regulator-subscriber" label="Taxpayer subscriber ID"><Input id="regulator-subscriber" name="subscriberId" defaultValue={grant.subscriberId}
              readOnly={!!grant.subscriberId} pattern={uuid} required /></Field>
            <Field id="regulator-company" label="Company ID"><Input id="regulator-company" name="companyId" defaultValue={grant.companyId}
              readOnly={!!grant.companyId} pattern={uuid} required /></Field>
            <Field id="regulator-branch" label="Branch ID (optional)"><Input id="regulator-branch" name="branchId" defaultValue={grant.branchId}
              readOnly={!!grant.branchId} pattern={uuid} /></Field>
            <Field id="regulator-rdo" label="Registered RDO (optional)"><Input id="regulator-rdo" name="rdo" defaultValue={grant.rdoCode ?? initialRdo}
              readOnly={!!grant.rdoCode} pattern="[0-9]{3}[A-Z]?" maxLength={4} /></Field>
            <Field id="regulator-from" label="From business date"><Input id="regulator-from" name="from" type="date" defaultValue={manilaDate()} required /></Field>
            <Field id="regulator-through" label="Through business date"><Input id="regulator-through" name="through" type="date" defaultValue={manilaDate()} required /></Field>
          </div>
          <Field id="regulator-category" label="Record category">
            <Select id="regulator-category" name="category">
              {grant.categories.map(category => <option key={category} value={category}>{categories[category]}</option>)}
            </Select>
          </Field>
          <div><Button type="submit" disabled={results.isFetching}>Search records</Button></div>
        </form>}
      {search && <section className="connect-stack" aria-labelledby="regulator-results">
        <h2 id="regulator-results">{categories[search.category]}</h2>
        {results.isFetching ? <LoadingState label="Searching scoped records" /> : results.isError ? <ErrorState>{errorMessage(results.error)}</ErrorState>
          : results.data && <RegulatorTable key={search.path + search.page} data={results.data} />}
        {results.isSuccess && !results.isFetching && <div className="connect-actions">
          <Button variant="light" disabled={search.page === 1} onClick={() => setSearch({ ...search, page: search.page - 1 })}>Previous</Button>
          <span>Page {search.page}</span>
          <Button variant="light" disabled={!results.data.hasMore} onClick={() => setSearch({ ...search, page: search.page + 1 })}>Next</Button>
          {search.canExport && <Button variant="light" disabled={busy} onClick={() => void download()}>Export this page as JSON</Button>}
        </div>}
      </section>}
    </div>
  </AccountShell>
}

function RegulatorTable({ data }: { data: RecordPage }) {
  if (data.items.length === 0) return <EmptyState title="No records in scope">Choose another authorized period or filter.</EmptyState>
  const columns = Object.keys(data.items[0])
  return <ScrollTable name="Regulator records">
    <thead><tr>{columns.map(column => <th key={column} scope="col">{column === 'vat' ? 'VAT' :
      (column[0].toUpperCase() + column.slice(1)).replace(/([a-z])([A-Z])/g, '$1 $2')}</th>)}</tr></thead>
    <tbody>{data.items.map((row, index) => <tr key={index}>{columns.map(column => <td key={column}>
      {column === 'at' && typeof row[column] === 'string' ? manilaTimestamp.format(new Date(row[column])) : row[column] ?? '—'}
    </td>)}</tr>)}</tbody>
  </ScrollTable>
}
