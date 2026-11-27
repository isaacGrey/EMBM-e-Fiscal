import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2, Database, FileText, LogOut, Settings, ShieldCheck, Users, Wallet } from 'lucide-react'
import { Link, NavLink, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { Brand } from '@/components/brand'
import { CompanyWizard } from '@/components/company-forms'
import { CompanyDeliverySettings } from './company-delivery'
import { DocumentsWorkspace } from './documents'
import { mayChangeDocumentContext } from './document-form'
import { Badge, Button, Card, ErrorState, Field, Input, LoadingState, Money, Select } from '@/components/ui'
import { api, errorMessage, sessionQuery, type ActiveContext, type Branch, type CompanyDetails, type CompanyList, type PendingSubscription, type RdoReference, type Session, type Subscriber, type WorkspaceData } from '@/lib/account-api'
import { billingPeriods, catalogQuery } from '@/lib/subscriptions'
import { BillingWorkspace, CheckoutButton, HostedCheckout } from './billing'
import { MasterData } from './master-data'
import { ConnectWorkspace } from './connect'
import { FieldMapping, ReferenceMapping } from './field-mapping'
import { Synchronization } from './accounting-sync'
import { ReconciliationWorkspace } from './reconciliation'
import { ReconciliationCoverage } from './reconciliation-coverage'
import { ReportingWorkspace } from './reporting'
import { RegulatoryAuthorizationWorkspace } from './regulatory-authorization'
import { BranchSettings, CompanySettings, RdoHistory, SecuritySettings, SettingsForm, TaxpayerSettings, TeamSettings, UserSettings } from './workspace-settings'
import './workspace.css'

const links = [
  { path: '', label: 'Overview', icon: Building2 },
  { path: 'subscriber', label: 'Subscriber Information', icon: Building2 },
  { path: 'subscription', label: 'Subscription', icon: Wallet, billing: true },
  { path: 'credits', label: 'Save & Send Credits', icon: Wallet, billing: true },
  { path: 'billing-history', label: 'Billing History', icon: FileText, billing: true },
  { path: 'company', label: 'Company Information', icon: FileText },
  { path: 'workstations', label: 'Invoicing Workstations', icon: FileText },
  { path: 'documents/SalesInvoice', label: 'Sales Invoices', icon: FileText },
  { path: 'documents/CreditMemo', label: 'Credit Memos', icon: FileText },
  { path: 'documents/SalesOrder', label: 'Sales Orders', icon: FileText },
  { path: 'documents/numbering', label: 'Document Numbering', icon: Settings },
  { path: 'documents/delivery', label: 'Email Delivery History', icon: FileText },
  { path: 'master-data', label: 'Master Data', icon: Database },
  { path: 'integrations/marketplace', label: 'Integration Marketplace', icon: Settings, integration: true },
  { path: 'integrations/connections', label: 'My Connections', icon: Database, integration: true },
  { path: 'integrations/field-mapping', label: 'Field Mapping', icon: Settings, integration: true, manage: true },
  { path: 'integrations/reference-mapping', label: 'Reference Mapping', icon: Database, integration: true, manage: true },
  { path: 'integrations/synchronization', label: 'Synchronization Center', icon: FileText, integration: true, manage: true },
  { path: 'integrations/company-files', label: 'External Company Files', icon: Building2, integration: true },
  { path: 'integrations/stores', label: 'Connected Stores', icon: Building2, integration: true },
  { path: 'integrations/source-orders', label: 'Source Orders', icon: FileText, integration: true, manage: true },
  { path: 'integrations/history', label: 'Connection History', icon: FileText, integration: true, manage: true },
  { path: 'integrations/reconciliation-settings', label: 'Reconciliation Settings', icon: Settings, integration: true, manage: true, fixture: true },
  { path: 'reconciliation', label: 'Reconciliation', icon: ShieldCheck, fixture: true },
  { path: 'reporting', label: 'BIR Reporting Preparation', icon: ShieldCheck },
  { path: 'regulatory-authorizations', label: 'Regulatory Authorizations', icon: ShieldCheck },
  { path: 'delivery', label: 'Delivery & Templates', icon: Settings },
  { path: 'taxpayer', label: 'Taxpayer Registration', icon: FileText },
  { path: 'rdo', label: 'Registered RDO', icon: FileText },
  { path: 'branches', label: 'Branch Management', icon: Building2 },
  { path: 'team', label: 'Team Members', icon: Users },
  { path: 'roles', label: 'Roles and Permissions', icon: ShieldCheck },
  { path: 'profile', label: 'User Profile', icon: Settings },
  { path: 'security', label: 'Password and Security', icon: ShieldCheck },
]
const root = '/portal/client'
const workspaceKey = (id: string) => ['workspace', id]

function PendingPayment({ pending }: { pending: PendingSubscription }) {
  const catalog = useQuery(catalogQuery)
  const plan = catalog.data?.plans.find(item => item.id === pending.planId)
  return <Card className="workspace-card">
    <Badge tone="warning">Pending Payment</Badge><h2>Subscription checkout</h2>
    <p>Your email is verified and your subscription selection is saved.</p>
    <dl className="workspace-definitions">
      <div><dt>Selected subscription</dt><dd>{plan?.name ?? pending.planId}</dd></div>
      <div><dt>Billing frequency</dt><dd>{billingPeriods[pending.billing].label}</dd></div>
      {plan && <div><dt>Illustrative amount</dt><dd><Money amount={plan.billing[pending.billing].price} /></dd></div>}
    </dl>
    <p>Company onboarding opens after verified payment and subscriber provisioning.</p>
    {catalog.data?.checkoutAvailable ? <CheckoutButton base="/billing" purpose="InitialSubscription" disabled={!plan}>Continue to checkout</CheckoutButton>
      : catalog.isPending ? <LoadingState label="Checking checkout availability…" /> : <p>Checkout is unavailable in this environment.</p>}
  </Card>
}

function CompanySelectors({ companies, selected, branches, setContext, busy, companyOnly }: { companies: CompanyList; selected: ActiveContext | null; branches: Branch[]; companyOnly: boolean;
    setContext: (company: string, branch?: string) => Promise<void>; busy: boolean }) {
  const [search, setSearch] = useState('')
  const matches = companies.companies.filter(item => item.id === selected?.companyId ||
    (item.code + ' ' + item.registeredName).toLowerCase().includes(search.trim().toLowerCase()))
  return <Card className="workspace-card selector-card">
    <div className="selector-grid">
      <Field id="company-search" label="Search Company Files"><Input id="company-search" type="search" value={search}
        onChange={event => setSearch(event.target.value)} placeholder="Company code or name" /></Field>
      <Field id="company-selector" label="Company File"><Select id="company-selector" value={selected?.companyId ?? ''} disabled={busy || !matches.length}
        onChange={event => void setContext(event.target.value)}>
        <option value="" disabled>Select a company file</option>
        {matches.map(item => <option key={item.id} value={item.id}>{item.code} · {item.registeredName}</option>)}
      </Select></Field>
      {selected && !companyOnly && <Field id="branch-selector" label="Branch"><Select id="branch-selector" disabled={busy} value={selected.branchId ?? ''}
        onChange={event => void setContext(selected.companyId, event.target.value)}>
        <option value="">All company branches</option>
        {branches.map(branch => <option value={branch.id} key={branch.id}>{branch.code} · {branch.name}</option>)}
      </Select></Field>}
    </div>
    {companies.canAddCompany && <Button variant="light" asChild><Link to={root + '/onboarding'}>Add Company File</Link></Button>}
  </Card>
}

function Overview({ details, subscriber, branches, active }: { details: CompanyDetails; subscriber: Subscriber; branches: Branch[]; active: ActiveContext }) {
  const rdo = details.assignments.find(item => !item.branchId && !item.effectiveUntil)
  return <Card className="workspace-card">
    <h2>{details.company.registeredName}</h2>
    <dl className="workspace-definitions">
      <div><dt>Company file</dt><dd>{details.company.code}</dd></div>
      <div><dt>Branch</dt><dd>{branches.find(branch => branch.id === active.branchId)?.name ?? 'All company branches'}</dd></div>
      <div><dt>Subscriber status</dt><dd>{subscriber.status}</dd></div>
      <div><dt>Company configuration</dt><dd>{details.configurationStatus}</dd></div>
      <div><dt>Registered RDO</dt><dd>{rdo ? rdo.code + ' · ' + rdo.name : 'Unassigned'}</dd></div>
      <div><dt>Regulatory verification</dt><dd>{details.taxpayer.verificationStatus === 'NotVerified' ? 'Not verified' : details.taxpayer.verificationStatus}</dd></div>
      <div><dt>Electronic invoice issuance</dt><dd>{details.taxpayer.issuanceStatus}</dd></div>
      <div><dt>Accounting integration</dt><dd>Setup unavailable in this phase</dd></div>
    </dl>
  </Card>
}

function SubscriberWorkspace({ subscriber, user, active, section }: { subscriber: Subscriber; user: Session; active: ActiveContext | null; section: string }) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const base = '/workspace/' + subscriber.id
  const companyPath = base + '/companies/' + active?.companyId
  const catalog = useQuery({ ...catalogQuery, enabled: section === 'subscriber' })
  const query = useQuery({ queryKey: ['companies', base], queryFn: () => api<CompanyList>(base + '/companies') })
  const details = useQuery({ queryKey: ['company', companyPath], queryFn: () => api<CompanyDetails>(companyPath), enabled: !!active })
  const branches = useQuery({ queryKey: ['branches', companyPath], queryFn: () => api<Branch[]>(companyPath + '/branches'), enabled: !!active })
  const rdos = useQuery({ queryKey: ['rdos', base], queryFn: () => api<RdoReference[]>(base + '/rdos'),
    enabled: ['onboarding', 'taxpayer', 'branches'].includes(section) })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function setContext(companyId: string, branchId?: string) {
    if (!mayChangeDocumentContext()) return
    setBusy(true)
    setError('')
    try {
      await api(base + '/context', 'POST', { companyId, branchId: branchId || null })
      await queryClient.invalidateQueries({ queryKey: workspaceKey(user.id) })
    } catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  async function refresh() {
    await Promise.all([query.refetch(), active ? details.refetch() : null, active ? branches.refetch() : null])
  }
  if (query.isPending) return <LoadingState label="Loading authorized company files…" />
  if (query.isError) return <ErrorState>{errorMessage(query.error)}</ErrorState>
  const companies = query.data
  const grants = companies.permissions
  const canEdit = grants.includes('Company.Manage')
  const masterSection = section === 'master-data' || section.startsWith('master-data/')
  if (['subscription', 'credits', 'billing-history'].includes(section))
    return grants.includes('Billing.View') ? <BillingWorkspace subscriber={subscriber.id} section={section} />
      : <ErrorState>Billing access is unavailable for your role.</ErrorState>
  if (section === 'onboarding') {
    if (!companies.canAddCompany) return <ErrorState>Company setup is unavailable for this account or company allowance.</ErrorState>
    if (rdos.isPending) return <LoadingState label="Loading registered RDO references…" />
    if (rdos.isError) return <ErrorState>{errorMessage(rdos.error)}</ErrorState>
    return <CompanyWizard path={base} rdos={rdos.data} complete={async () => {
      await queryClient.invalidateQueries({ queryKey: workspaceKey(user.id) }); await query.refetch(); navigate(root)
    }} />
  }
  let content
  if (section === 'subscriber') {
    return subscriber.roleId === 'owner'
      ? <SettingsForm title="Subscriber Information" action="Save subscriber information" submit={async form => {
          await api(base, 'PUT', { name: form.get('name') })
          await queryClient.invalidateQueries({ queryKey: workspaceKey(user.id) })
        }}>
          <Field id="subscriber-name" label="Subscriber Name"><Input id="subscriber-name" name="name" defaultValue={subscriber.name} maxLength={150} required /></Field>
          <p>Subscription · {catalog.data?.plans.find(plan => plan.id === subscriber.planId)?.name ?? subscriber.planId} · {subscriber.status}</p>
        </SettingsForm>
      : <Card className="workspace-card"><h2>{subscriber.name}</h2><p>Subscriber status · {subscriber.status}</p></Card>
  }
  if (section === 'roles') {
    content = <Card className="workspace-card"><h2>Roles and Permissions</h2><p>Assigned role · {subscriber.roleId}</p>
      <ul className="grants-grid">{grants.map(grant => <li key={grant}>{grant}</li>)}</ul>
      <p>Financial operations are unavailable in this phase.</p></Card>
  } else if (section === 'team') {
    content = grants.includes('Users.Invite') ? <TeamSettings path={base + '/team'} companies={companies.companies} /> : <ErrorState>Team management is unavailable for your role.</ErrorState>
  } else if (!active) {
    content = <Card className="workspace-card"><h2>{companies.companies.length ? 'Choose a company file' : 'Set up your first company file'}</h2>
      <p>{companies.companies.length ? 'Select an authorized company to work with its settings.' : companies.canAddCompany
        ? 'Add a company file to register its business information, taxpayer details, and first branch.' : 'You have no assigned company files.'}</p></Card>
  } else if (details.isPending || branches.isPending || (rdos.isEnabled && rdos.isPending)) {
    content = <LoadingState label="Loading company settings…" />
  } else if (details.isError || branches.isError || rdos.isError) {
    content = <ErrorState>{errorMessage(details.error ?? branches.error ?? rdos.error)}</ErrorState>
  } else {
    const config = { path: companyPath, details: details.data, saved: refresh }
    if (masterSection)
      content = <MasterData key={companyPath} base={companyPath + '/master-data'}
        section={section.split('/')[1] ?? 'customers'} />
    else if (section === 'delivery' || section.startsWith('delivery/')) content = canEdit
      ? <CompanyDeliverySettings key={companyPath + section} companyPath={companyPath} initialSection={section.split('/')[1]} initialType={section.split('/')[2]} />
      : <ErrorState>Delivery settings require Company.Manage permission.</ErrorState>
    else if (section === 'workstations' || section === 'documents' || section.startsWith('documents/'))
      content = <DocumentsWorkspace key={companyPath} base={companyPath + '/documents'} companyPath={companyPath} section={section} branch={active.branchId ?? undefined} />
    else if (section === 'regulatory-authorizations') content = grants.includes('Regulatory.Evidence.View')
      ? <RegulatoryAuthorizationWorkspace key={companyPath} base={companyPath + '/regulatory-authorizations'} branches={branches.data ?? []} />
      : <ErrorState>Authorization evidence is unavailable for your role.</ErrorState>
    else if (section === 'reporting') content = grants.includes('Reports.View')
      ? <ReportingWorkspace key={companyPath} base={companyPath + '/reporting'} /> : <ErrorState>Reporting access is unavailable for your role.</ErrorState>
    else if (section === 'reconciliation' || section.startsWith('reconciliation/'))
      content = subscriber.developmentFixture && grants.includes('Reconciliation.View') ?
        <ReconciliationWorkspace key={companyPath} base={companyPath + '/connect/reconciliation'} section={section.slice('reconciliation/'.length)}
          branches={branches.data} selectedBranch={active.branchId ?? undefined} permissions={grants} /> : <ErrorState>Reconciliation access is unavailable.</ErrorState>
    else if (section.startsWith('integrations/reconciliation-settings'))
      content = subscriber.developmentFixture && grants.includes('Reconciliation.ManageCoverage') ?
        <ReconciliationCoverage key={companyPath + section} base={companyPath + '/connect/reconciliation'} initialConnection={section.split('/')[2]} /> :
        <ErrorState>Coverage management is unavailable for your role.</ErrorState>
    else if (section.startsWith('integrations/field-mapping'))
      content = <FieldMapping base={companyPath + '/connect'} id={section.split('/')[2]} />
    else if (section === 'integrations/reference-mapping')
      content = <ReferenceMapping base={companyPath + '/connect'} />
    else if (section.startsWith('integrations/synchronization'))
      content = <Synchronization base={companyPath + '/connect'} id={section.split('/')[2]} branches={branches.data ?? []} />
    else if (section === 'integrations' || section.startsWith('integrations/'))
      content = <ConnectWorkspace key={companyPath} base={companyPath + '/connect'} master={companyPath + '/master-data'}
        section={section} branches={branches.data ?? []} branch={active.branchId ?? undefined} />
    else if (section === 'company' && canEdit) content = <CompanySettings key={companyPath} {...config} />
    else if (section === 'taxpayer' && canEdit) content = <TaxpayerSettings key={companyPath} {...config} rdos={rdos.data ?? []} />
    else if (section === 'rdo') content = <RdoHistory details={details.data} />
    else if (section === 'branches') content = <BranchSettings key={companyPath} {...config} branches={branches.data} rdos={rdos.data ?? []} editable={grants.includes('Branch.Manage')} />
    else if (section === 'company' || section === 'taxpayer') content = <ErrorState>Editing these settings requires Company.Manage permission.</ErrorState>
    else content = <Overview details={details.data} subscriber={subscriber} branches={branches.data} active={active} />
  }
  return <>
    <CompanySelectors key={subscriber.id} companies={companies} selected={active} branches={branches.data ?? []} setContext={setContext} busy={busy} companyOnly={masterSection} />
    {error && <ErrorState>{error}</ErrorState>}
    {content}
  </>
}

function WorkspaceShell({ user }: { user: Session }) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const section = pathname.slice(root.length + 1)
  const checkoutId = section.match(/^checkout\/([\da-f-]{36})$/i)?.[1]
  const query = useQuery({ queryKey: workspaceKey(user.id), queryFn: () => api<WorkspaceData>('/workspace') })
  const [selected, setSelected] = useState('')
  const [error, setError] = useState('')
  const activeSubscribers = query.data?.subscribers.filter(subscriber => subscriber.status === 'Active') ?? []
  const subscriber = activeSubscribers.find(item => item.id === selected) ??
    activeSubscribers.find(item => item.id === query.data?.active?.subscriberId) ?? activeSubscribers[0]

  async function signedOut() {
    queryClient.clear()
    queryClient.setQueryData(sessionQuery.queryKey, null)
    navigate('/sign-in', { replace: true })
  }
  async function logout() {
    if (!mayChangeDocumentContext()) return
    try { await api('/auth/logout', 'POST'); await signedOut() }
    catch (failure) { setError(errorMessage(failure)) }
  }

  return <main className="portal client-workspace" id="main-content">
    <aside className="portal-sidebar"><Brand /><p>Client Business Portal</p>
      <nav aria-label="Client workspace">{links.filter(link => !link.integration && (!link.fixture || subscriber?.developmentFixture && subscriber.roleId !== 'encoder' && subscriber.roleId !== 'readonly') && (!link.path ||
        ['profile', 'security'].includes(link.path) || (!!subscriber && (!link.billing || subscriber.roleId === 'owner'))))
        .map(({ path, label, icon: Icon }) => <NavLink key={label} end={!path} to={root + (path ? '/' + path : '')}><Icon size={16} />{label}</NavLink>)}</nav>
      {subscriber && <nav aria-label="Integrations"><span className="connect-nav-title">INTEGRATIONS</span>
        {links.filter(link => link.integration && (!link.fixture || subscriber.developmentFixture) && (!link.manage || ['owner', 'company-admin'].includes(subscriber.roleId)))
          .map(({ path, label, icon: Icon }) => <NavLink key={path} to={root + '/' + path}><Icon size={16} />{label}</NavLink>)}</nav>}
    </aside>
    <div className="portal-main">
      <header className="portal-header"><span>{subscriber?.developmentFixture ? <Badge tone="warning">Development subscriber</Badge> : <Badge>Client workspace</Badge>}</span>
        <Button variant="light" type="button" onClick={() => void logout()}><LogOut size={16} /> Sign Out</Button></header>
      <div className="portal-content workspace-stack">
        <h1>{checkoutId ? 'Checkout' : section ? links.find(link => link.path && (link.path === section || section.startsWith(link.path + '/')))?.label ?? 'Company setup' : 'Welcome, ' + user.fullName}</h1>
        {error && <ErrorState>{error}</ErrorState>}
        {activeSubscribers.length > 1 && <Field id="subscriber-selector" label="Subscriber"><Select id="subscriber-selector" value={subscriber?.id ?? ''}
          onChange={event => { if (mayChangeDocumentContext()) setSelected(event.target.value) }}>{activeSubscribers.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</Select></Field>}
        {checkoutId ? <HostedCheckout key={checkoutId} id={checkoutId} /> : section === 'profile' ? <UserSettings user={user} refresh={async () => { await queryClient.invalidateQueries({ queryKey: sessionQuery.queryKey }) }} />
          : section === 'security' ? <SecuritySettings signedOut={signedOut} />
          : query.isPending ? <LoadingState label="Loading subscriber status…" />
          : query.isError ? <ErrorState>{errorMessage(query.error)}</ErrorState>
          : subscriber ? <SubscriberWorkspace key={subscriber.id} subscriber={subscriber} user={user} section={section}
            active={query.data.active?.subscriberId === subscriber.id ? query.data.active : null} />
          : query.data.pending ? <PendingPayment pending={query.data.pending} />
          : <Card className="workspace-card"><h2>Subscriber access unavailable</h2>
            <p>{query.data.subscribers.length ? 'Your subscriber access is not active.' : 'You can join a subscriber using an invitation from its Account Owner.'}</p></Card>}
      </div>
    </div>
  </main>
}

export function ClientWorkspace() {
  const session = useQuery(sessionQuery)
  if (session.isPending || (session.isFetching && !session.isFetchedAfterMount)) return <main id="main-content" className="container section"><LoadingState label="Checking session…" /></main>
  if (session.isError) return <main id="main-content" className="container section"><ErrorState>{errorMessage(session.error)}</ErrorState>
    <Button variant="light" onClick={() => void session.refetch()}>Try again</Button></main>
  return session.data?.kind === 'Customer' ? <WorkspaceShell key={session.data.id} user={session.data} /> : <Navigate to="/sign-in" replace />
}
