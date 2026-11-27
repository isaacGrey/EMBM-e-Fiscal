import { useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2, ChevronLeft, Cloud, Plug, Search, Store } from 'lucide-react'
import { Link } from 'react-router-dom'
import { Badge, Button, Card, ErrorState, Field, Input, LoadingState, Select, Table } from '@/components/ui'
import { api, errorMessage, manilaTimestamp, type Branch } from '@/lib/account-api'
import { connectRoot, invoiceFromSource, type Authorization, type Connection, type ConnectionLog, type ConnectorCard, type MappingInfo, type ProviderOrder, type SourceOrder } from '@/lib/connect-api'
import { Reference } from './master-fields'
import './connect.css'
import { coverageRoot } from '@/lib/reconciliation-api'

const catalogPath = connectRoot + '/marketplace'
const time = (value: string) => manilaTimestamp.format(new Date(value))
const label = (value: string) => value.replace(/([a-z])([A-Z])/g, '$1 $2')
const money = (value: number) => new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' }).format(value)
type Settings = { base: string; master: string; branches: Branch[]; branch?: string }
type Rows<T> = { items: T[] }

function useAction() {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  async function run(action: () => Promise<void>) {
    setBusy(true); setError(''); setNotice('')
    try { await action() }
    catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  return { busy, error, notice, setNotice, run }
}

export function ConnectWorkspace(settings: Settings & { section: string }) {
  const [, view = 'marketplace', id, action] = settings.section.split('/')
  if (view === 'marketplace') return id
    ? <MarketplaceDetail key={id} {...settings} code={id} wizard={action === 'connect'} />
    : <Marketplace base={settings.base} />
  if (view === 'source-orders') return id ? <SourceDetail key={id} id={id} base={settings.base} /> : <SourceOrders base={settings.base} />
  if (view === 'history') return <History base={settings.base} />
  if (view === 'connections' && id) return <ConnectionDetail key={id} {...settings} id={id} />
  return <Connections base={settings.base} mode={view} />
}

function ConnectorMark({ connector }: { connector: ConnectorCard }) {
  const Icon = connector.category === 'E-Commerce' ? Store : connector.deployment.includes('Desktop') ? Building2 : Cloud
  return <span className="connect-mark" aria-hidden="true">{connector.logoLabel || <Icon size={20} />}</span>
}
function Marketplace({ base }: { base: string }) {
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState('')
  const query = useQuery({ queryKey: ['connect', base, 'catalog'], queryFn: () => api<Rows<ConnectorCard>>(base + '/catalog'), retry: false })
  if (query.isPending) return <LoadingState label="Loading connector catalog…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  const categories = [...new Set(query.data.items.map(x => x.category))]
  const term = search.trim().toLowerCase()
  const items = query.data.items.filter(x => (!category || x.category === category) &&
    [x.name, x.description, x.code, ...x.editions].join(' ').toLowerCase().includes(term))
  return <div className="connect-stack">
    <Card className="workspace-card connect-intro"><span className="connect-mark"><Plug size={22} /></span><div>
      <h2>EMBM eFiscal Connect</h2><p>Find accounting systems and stores that can supply data for your company’s invoicing workflows.</p>
    </div></Card>
    <Card className="workspace-card"><div className="connect-grid">
      <Field id="connect-search" label="Search integrations"><Input id="connect-search" type="search" value={search} onChange={e => setSearch(e.target.value)} placeholder="Platform or edition" /></Field>
      <Field id="connect-filter" label="Category"><Select id="connect-filter" value={category} onChange={e => setCategory(e.target.value)}>
        <option value="">All categories</option>{categories.map(value => <option key={value}>{value}</option>)}</Select></Field>
    </div></Card>
    {items.length === 0 ? <p>No connectors match these filters.</p> : <div className="connect-cards">{items.map(item => <Card className="workspace-card connect-card" key={item.code}>
      <div className="connect-title"><ConnectorMark connector={item} /><div><h3>{item.name}</h3><p>{item.category}</p></div></div>
      <div className="connect-actions"><Badge tone={item.development ? 'warning' : 'neutral'}>{item.availability}</Badge>
        {item.priority === 'Primary' && <Badge>Priority</Badge>}</div>
      <p>{item.description}</p>
      <div className="connect-actions"><Button asChild variant="light"><Link to={catalogPath + '/' + item.code}>View Details<span className="sr-only"> for {item.name}</span></Link></Button>
      {item.canConnect && <Button asChild><Link to={catalogPath + '/' + item.code + '/connect'}>Connect<span className="sr-only"> {item.name}</span></Link></Button>}</div>
    </Card>)}</div>}
  </div>
}

function MarketplaceDetail(settings: Settings & { code: string; wizard: boolean }) {
  const query = useQuery({ queryKey: ['connect', settings.base, 'catalog', settings.code],
    queryFn: () => api<ConnectorCard>(settings.base + '/catalog/' + settings.code), retry: false })
  if (query.isPending) return <LoadingState label="Loading integration…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  const item = query.data
  return <div className="connect-stack"><Link className="back-link" to={catalogPath}><ChevronLeft size={16} /> Back to marketplace</Link>
    <Card className="workspace-card"><div className="connect-title"><ConnectorMark connector={item} /><div>
      <h2>{item.name}</h2><p>{item.category} · Version {item.version}</p></div></div>
      <Badge tone={item.development ? 'warning' : 'neutral'}>{item.availability}</Badge><p>{item.description}</p>
      <dl className="workspace-definitions">
        <div><dt>Implementation</dt><dd>{item.deployment}</dd></div><div><dt>Editions</dt><dd>{item.editions.join(', ') || 'Pending verification'}</dd></div>
        <div><dt>Regions</dt><dd>{item.regions.join(', ') || 'Pending verification'}</dd></div><div><dt>Authorization</dt><dd>{item.authentication.join(', ') || 'Pending verification'}</dd></div>
        <div><dt>API version</dt><dd>{item.apiVersion || 'Pending verification'}</dd></div>
      </dl>
      <h3>Connection requirements</h3><ul>{item.requirements.map(value => <li key={value}>{value}</li>)}</ul>
      {!!item.documentation.length && <ul>{item.documentation.map((link, index) => <li key={link}><a className="text-link" href={link} target="_blank" rel="noreferrer">Provider documentation {index + 1}</a></li>)}</ul>}
      {item.canConnect && !settings.wizard && <Button asChild><Link to={catalogPath + '/' + item.code + '/connect'}>Connect</Link></Button>}
      {!item.canConnect && <p>Connection setup is unavailable for the current company.</p>}
    </Card>
    {item.canConnect && settings.wizard && <CompanyWizard {...settings} connector={item} />}
    <Card className="workspace-card"><h3>Capabilities</h3>{item.capabilities.length ? <ul className="connect-capabilities">
      {item.capabilities.map(cap => <li key={cap.code}>{label(cap.code)} <Badge tone={cap.verification === 'Development Verified' ? 'success' : 'neutral'}>{cap.verification}</Badge></li>)}
    </ul> : <p>Capability verification is pending.</p>}</Card>
  </div>
}

function CompanyWizard({ base, connector, branches, branch }: Settings & { connector: ConnectorCard }) {
  const [authorization, setAuthorization] = useState<Authorization>()
  const [resource, setResource] = useState('')
  const [selectedBranch, setSelectedBranch] = useState(branch ?? '')
  const [name, setName] = useState('')
  const [binding, setBinding] = useState<Connection>()
  const action = useAction()
  const store = connector.category === 'E-Commerce'
  return <Card className="workspace-card connect-stack">
    <h3>Connect {store ? 'a development store' : 'a development company file'}</h3>
    <p>Use a fictional account for this connection. Setup requires an external resource, a successful test and a company mapping.</p>
    {!authorization && <div><Button type="button" disabled={action.busy} onClick={() => void action.run(async () =>
      setAuthorization(await api<Authorization>(base + '/catalog/' + connector.code + '/authorize', 'POST')))}>Authorize demo account</Button></div>}
    {authorization && !binding && <form className="connect-stack" onSubmit={event => {
      event.preventDefault()
      void action.run(async () => setBinding(await api<Connection>(base + '/connections', 'POST', {
        versionId: authorization.versionId, proof: authorization.proof, resourceId: resource, name, branchId: selectedBranch || null,
      })))
    }}>
      <p>Authorized account {authorization.account.name}</p>
      <div className="connect-grid">
        <Field id="connect-resource" label={store ? 'External store' : 'External company file'}><Select id="connect-resource" value={resource} required onChange={e => {
          setResource(e.target.value); setName(authorization.account.resources.find(x => x.id === e.target.value)?.name ?? '')
        }}><option value="">Select a resource</option>{authorization.account.resources.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field>
        <Field id="connect-name" label="Connection name"><Input id="connect-name" value={name} required maxLength={100} onChange={e => setName(e.target.value)} /></Field>
        <Field id="connect-branch" label="Branch scope"><Select id="connect-branch" value={selectedBranch} onChange={e => setSelectedBranch(e.target.value)}>
          <option value="">Company-wide</option>{branches.map(x => <option value={x.id} key={x.id}>{x.code} · {x.name}</option>)}</Select></Field>
      </div>
      <div><Button type="submit" disabled={action.busy}>Bind to this eFiscal company</Button></div>
    </form>}
    {binding && <div className="connect-actions"><Badge tone="success">Company binding created</Badge>
      <Button asChild><Link to={connectRoot + '/connections/' + binding.id}>Continue to test and mapping</Link></Button></div>}
    {action.error && <ErrorState>{action.error}</ErrorState>}
  </Card>
}

function Connections({ base, mode }: { base: string; mode: string }) {
  const query = useQuery({ queryKey: ['connect', base, 'connections'], queryFn: () => api<Rows<Connection> & { canManage: boolean }>(base + '/connections'), retry: false })
  if (query.isPending) return <LoadingState label="Loading company connections…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  const rows = query.data.items.filter(x => mode === 'stores' ? x.resourceType === 'Store' : mode === 'company-files' ? x.resourceType === 'CompanyFile' : true)
  return <div className="connect-stack"><Card className="workspace-card"><h2>{mode === 'stores' ? 'Connected stores' : mode === 'company-files' ? 'External company files' : 'Company connections'}</h2>
    <p>Connections are scoped to the selected eFiscal company and pinned to their approved connector version.</p>
    <Link className="text-link" to={catalogPath}>Browse Integration Marketplace</Link>
  </Card>{rows.length ? <Card className="workspace-card"><ScrollTable name="Company connections">
    <thead><tr><th>Connection</th><th>External resource</th><th>Status</th><th>Version</th></tr></thead>
    <tbody>{rows.map(x => <tr key={x.id}><td>{query.data.canManage
      ? <Link className="text-link" to={connectRoot + '/connections/' + x.id}>{x.name}</Link> : x.name}</td>
      <td>{x.externalResourceName}</td><td><Badge tone={x.status === 'Connected' ? 'success' : 'neutral'}>{x.status}</Badge></td>
      <td>{x.connector} · v{x.version}{x.development && <div><Badge tone="warning">Development connection</Badge></div>}</td></tr>)}</tbody>
  </ScrollTable></Card> : <Card className="workspace-card"><p>No connections in this view.</p></Card>}</div>
}

function ConnectionDetail({ base, master, id }: Settings & { id: string }) {
  const [scenario, setScenario] = useState('success')
  const [search, setSearch] = useState('')
  const [orders, setOrders] = useState<{ order: ProviderOrder; key: string | null }[]>()
  const client = useQueryClient()
  const action = useAction()
  const list = useQuery({ queryKey: ['connect', base, 'connections'],
    queryFn: () => api<Rows<Connection> & { canManage: boolean }>(base + '/connections'), retry: false })
  if (list.isPending) return <LoadingState label="Loading connection…" />
  if (list.isError) return <ErrorState>{errorMessage(list.error)}</ErrorState>
  const link = list.data.items.find(x => x.id === id)
  if (!link || !list.data.canManage) return <ErrorState>This company connection is unavailable.</ErrorState>
  const path = base + '/connections/' + link.id
  async function refresh() { await client.invalidateQueries({ queryKey: ['connect', base] }) }
  function post(method: string, body?: object, message?: string) {
    void action.run(async () => {
      await api(path + '/' + method, 'POST', body)
      await refresh()
      if (message) action.setNotice(message)
    })
  }
  return <div className="connect-stack">
    <Link className="back-link" to={connectRoot + '/connections'}><ChevronLeft size={16} /> Back to connections</Link>
    <Card className="workspace-card"><h2>{link.name}</h2><div className="connect-actions">
      <Badge tone={link.status === 'Connected' ? 'success' : 'warning'}>{link.status}</Badge>
      {link.development && <Badge tone="warning">Development connection</Badge>}</div>
      <dl className="workspace-definitions">
        <div><dt>External resource</dt><dd>{link.externalResourceName}</dd></div><div><dt>Connector version</dt><dd>{link.connector} · v{link.version}</dd></div>
        <div><dt>Authorization</dt><dd>{link.authorization}</dd></div><div><dt>Mapping</dt><dd>{link.mappingStatus}{link.mappingVersion ? ' · v' + link.mappingVersion : ''}</dd></div>
        <div><dt>Last successful test</dt><dd>{link.lastSuccessfulTest ? time(link.lastSuccessfulTest) : '—'}</dd></div>
        {link.lastError && <div><dt>Last test result</dt><dd>{label(link.lastError)}</dd></div>}
      </dl>
      {link.development && link.resourceType === 'CompanyFile' && <Link className="text-link" to={coverageRoot + '/' + link.id}>Configure reconciliation coverage</Link>}
      {action.error && <ErrorState>{action.error}</ErrorState>}{action.notice && <p role="status">{action.notice}</p>}
      {link.status !== 'Suspended' && link.authorization === 'Authorized' && <div className="connect-actions">
        <Field id="mock-test" label="Development test scenario"><Select id="mock-test" value={scenario} onChange={e => setScenario(e.target.value)}>
          <option value="success">Successful connection</option><option value="authentication">Failed authentication</option>
          <option value="timeout">Timeout</option><option value="capability">Unavailable capability</option></Select></Field>
        <Button disabled={action.busy} onClick={() => post('test', { scenario })}>Test connection</Button>
        {link.status !== 'Connected' && <Button disabled={action.busy || !link.mappingVersion || !link.lastSuccessfulTest} onClick={() => post('activate')}>Activate connection</Button>}
      </div>}
      <div className="connect-actions">
        {link.status !== 'Disconnected' && <Button variant="light" disabled={action.busy} onClick={() => post('disconnect')}>Disconnect</Button>}
        {link.status !== 'Suspended' && link.authorization !== 'Authorized' && <Button variant="light" disabled={action.busy} onClick={() => void action.run(async () => {
          const authorization = await api<Authorization>(base + '/catalog/' + link!.connectorCode + '/authorize?connection=' + id, 'POST')
          await api(path + '/reauthorize', 'POST', { proof: authorization.proof }); await refresh()
        })}>Reauthorize demo account</Button>}
      </div>
    </Card>
    {link.authorization === 'Authorized' && link.status !== 'Suspended' && <Mapping key={link.mappingVersion ?? 0} path={path} master={master} link={link} refresh={refresh} />}
    {link.status === 'Connected' && (link.resourceType === 'Store' ?
      <Card className="workspace-card"><h3>Orders from this store</h3><p>Import eligible synthetic records to the source order register.</p>
        <div className="connect-actions"><Button disabled={action.busy} onClick={() => void action.run(async () => {
          const result = await api<{ imported: number; alreadyPresent: number; updated: number }>(path + '/import-orders', 'POST')
          await refresh(); action.setNotice(result.imported + ' imported · ' + result.alreadyPresent + ' already present' +
            (result.updated ? ' · ' + result.updated + ' status updates' : ''))
        })}>Retrieve marketplace orders</Button>
          <Button variant="light" disabled={action.busy} onClick={() => void action.run(async () => {
            const result = await api<{ updated: number; imported: number }>(path + '/import-orders?scenario=RefundUpdate', 'POST')
            await refresh(); action.setNotice(result.updated + ' status updates · ' + result.imported + ' imported')
          })}>Test a source refund update</Button><Link className="text-link" to={connectRoot + '/source-orders'}>Open Source Orders</Link></div>
      </Card> :
      <Card className="workspace-card"><h3>External Sales Orders</h3>
        <form className="connect-actions" onSubmit={event => {
          event.preventDefault()
          void action.run(async () => setOrders((await api<Rows<{ order: ProviderOrder; key: string | null }>>(path + '/sales-orders?q=' + encodeURIComponent(search))).items))
        }}><Field id="external-reference" label="External Sales Order number"><Input id="external-reference" type="search" value={search}
          onChange={e => setSearch(e.target.value)} placeholder="Example EXT-SO-MNL-1001" maxLength={70} /></Field>
          <Button type="submit" disabled={action.busy}><Search size={15} /> Find Sales Orders</Button></form>
        {orders && (orders.length ? orders.map(({ order, key }) => <details className="connect-order" key={order.id}>
          <summary>{order.reference} · {order.status}</summary><OrderSummary order={order} />
          {key && <Button asChild><Link to={invoiceFromSource(key)}>Use in Sales Invoice</Link></Button>}
        </details>) : <p>No matching Sales Orders.</p>)}
      </Card>)}
  </div>
}

function Mapping({ path, master, link, refresh }: { path: string; master: string; link: Connection; refresh: () => Promise<void> }) {
  const query = useQuery({ queryKey: ['connect-mapping', path, link.mappingVersion], queryFn: () => api<MappingInfo>(path + '/mapping'), retry: false })
  if (query.isPending) return <LoadingState label="Loading field metadata…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  return <MappingForm path={path} master={master} link={link} refresh={refresh} metadata={query.data} />
}

function MappingForm({ path, master, link, refresh, metadata }: { path: string; master: string; link: Connection; refresh: () => Promise<void>; metadata: MappingInfo }) {
  const saved = metadata.versions[0]
  const [customer, setCustomer] = useState(saved?.customerId ?? '')
  const [item, setItem] = useState(saved?.itemId ?? '')
  const [unit, setUnit] = useState(saved?.unitId ?? '')
  const action = useAction()
  return <Card className="workspace-card"><h3>Company field mapping</h3><p>{metadata.template} maps fixture codes to this company’s records.</p>
    <form className="connect-stack" onSubmit={event => {
      event.preventDefault()
      void action.run(async () => {
        await api(path + '/mapping', 'PUT', { customerId: customer, itemId: item, unitId: unit, expectedVersion: link.mappingVersion ?? 0 })
        await refresh()
      })
    }}><div className="connect-grid">
      <Reference base={master} id="map-customer" kind="customers" label="Company customer" required value={customer} onChange={setCustomer} />
      <Reference base={master} id="map-item" kind="items" label="Company item" required value={item} onChange={setItem} />
      <Reference base={master} id="map-unit" kind="units" label="Company base unit" required value={unit} onChange={setUnit} />
    </div>{action.error && <ErrorState>{action.error}</ErrorState>}
      <div><Button type="submit" disabled={action.busy}>Save mapping version</Button></div></form>
    <details className="connect-order"><summary>External field metadata</summary><ScrollTable name="External field metadata">
      <thead><tr><th>Location</th><th>Field</th><th>Type</th><th>Required</th><th>Allowed values</th></tr></thead>
      <tbody>{metadata.fields.map(x => <tr key={x.location + x.name}><td>{x.location}</td><td>{x.name}</td><td>{x.type}</td>
        <td>{x.required ? 'Yes' : 'No'}</td><td>{x.allowedValues?.join(', ') ?? '—'}</td></tr>)}</tbody>
    </ScrollTable></details>
  </Card>
}

function SourceOrders({ base }: { base: string }) {
  const [search, setSearch] = useState('')
  const query = useQuery({ queryKey: ['connect', base, 'source-orders'], queryFn: () => api<Rows<SourceOrder>>(base + '/source-orders'), retry: false })
  if (query.isPending) return <LoadingState label="Loading source orders…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  const rows = query.data.items.filter(x => [x.shop, x.reference, x.status].join(' ').toLowerCase().includes(search.trim().toLowerCase()))
  return <Card className="workspace-card connect-stack"><h2>Marketplace source orders</h2>
    <p>These records supply order references for a new Sales Invoice. Select an eligible order to begin invoicing.</p>
    <Field id="source-filter" label="Search source orders"><Input id="source-filter" type="search" value={search} onChange={e => setSearch(e.target.value)} /></Field>
    {rows.length ? <ScrollTable name="Marketplace source order register">
      <thead><tr><th>Reference</th><th>Store</th><th>Order date</th><th>Status</th><th>Currency</th></tr></thead>
      <tbody>{rows.map(row => <tr key={row.id}><td><Link className="text-link" to={connectRoot + '/source-orders/' + row.id}>{row.reference}</Link></td>
        <td>{row.shop}</td><td>{row.date}</td><td>{row.status}</td><td>{row.currency}</td></tr>)}</tbody>
    </ScrollTable> : <p>No source orders in this view.</p>}</Card>
}

function SourceDetail({ base, id }: { base: string; id: string }) {
  const query = useQuery({ queryKey: ['connect', base, 'source-orders', id],
    queryFn: () => api<{ record: SourceOrder; relatedInvoices: { id: string; number: string }[] }>(base + '/source-orders/' + id), retry: false })
  if (query.isPending) return <LoadingState label="Loading source order…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  const row = query.data.record
  return <Card className="workspace-card connect-stack"><Link className="back-link" to={connectRoot + '/source-orders'}><ChevronLeft size={16} /> Back to Source Orders</Link>
    <h2>{row.reference}</h2><p>{row.provider} · {row.shop} · {row.status}</p><p>Retrieved {time(row.retrievedAt)}</p>
    <OrderSummary order={row.order} />
    {row.key && query.data.relatedInvoices.length === 0 && <div><Button asChild><Link to={invoiceFromSource(row.key)}>Use in Sales Invoice</Link></Button></div>}
    {!!query.data.relatedInvoices.length && <div><h3>Related Sales Invoices</h3><ul>{query.data.relatedInvoices.map(invoice =>
      <li key={invoice.id}><Link className="text-link" to={'/portal/client/documents/SalesInvoice/' + invoice.id}>{invoice.number}</Link></li>)}</ul></div>}
  </Card>
}

function OrderSummary({ order }: { order: ProviderOrder }) {
  return <div className="connect-stack">
    <p>{order.customerName} · {order.orderedOn} · {order.currency}</p>
    <ScrollTable name="External order lines"><thead><tr><th>Item</th><th>Quantity</th><th>Invoiceable</th><th>Unit price</th><th>Discount</th><th>Tax reference</th></tr></thead>
      <tbody>{order.lines.map(line => <tr key={line.id}><td>{line.itemCode} · {line.description}</td><td>{line.quantity}</td>
        <td>{line.invoiceableQuantity}</td><td>{money(line.price)}</td><td>{money(line.discount)}</td><td>{line.taxCode ?? '—'}</td></tr>)}</tbody></ScrollTable>
    {order.shipping > 0 && <p>Source shipping charge {money(order.shipping)}</p>}
    {order.refund > 0 && <p>Source refund {money(order.refund)}</p>}
  </div>
}

function History({ base }: { base: string }) {
  const query = useQuery({ queryKey: ['connect', base, 'history'], queryFn: () => api<Rows<ConnectionLog>>(base + '/history'), retry: false })
  if (query.isPending) return <LoadingState label="Loading connection history…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  return <Card className="workspace-card"><h2>Company connection history</h2>{query.data.items.length ?
    <ScrollTable name="Connection audit history"><thead><tr><th>Connection</th><th>Event</th><th>Time</th></tr></thead>
      <tbody>{query.data.items.map((row, index) => <tr key={row.id + row.at + index}><td>{row.connection}</td>
        <td>{label(row.action.replace('Connections.', ''))}</td><td>{time(row.at)}</td></tr>)}</tbody></ScrollTable> : <p>No connection activity yet.</p>}
  </Card>
}

export function ScrollTable({ name, children }: { name: string; children: ReactNode }) {
  return <div className="connect-table" role="region" aria-label={name} tabIndex={0}><Table>{children}</Table></div>
}

