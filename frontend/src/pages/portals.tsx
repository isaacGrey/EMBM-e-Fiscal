import { useQuery } from '@tanstack/react-query'
import { ArrowLeft, ArrowRight, Building2, FileText, LayoutDashboard, LockKeyhole, Settings, ShieldCheck, Users, Wallet } from 'lucide-react'
import { Link } from 'react-router-dom'
import { Brand } from '@/components/brand'
import { Badge, Button, Card, Dialog, DialogContent, DialogTrigger, EmptyState, ErrorState, Field, Input, LoadingState } from '@/components/ui'
import { WorkspacePreview } from '@/components/workspace-preview'
import { healthQuery } from '@/lib/platform'

export const portals = {
  client: { title: 'Client Business Portal', signIn: '/sign-in', url: '/preview/client', icon: Building2,
    sections: [[LayoutDashboard, 'Overview'], [FileText, 'Documents'], [Wallet, 'Credit Usage History'], [Settings, 'Company settings']] },
  vendor: { title: 'Vendor Super Admin Portal', signIn: '/vendor/sign-in', url: '/portal/vendor', icon: Settings,
    sections: [[LayoutDashboard, 'Overview'], [Building2, 'Subscribers'], [Wallet, 'Subscriptions'], [Users, 'Permissions']] },
  bir: { title: 'BIR Read-Only Portal', signIn: '/bir/sign-in', url: '/portal/bir', icon: ShieldCheck,
    sections: [[LayoutDashboard, 'Overview'], [FileText, 'Document search'], [Building2, 'Registered RDOs']] },
} as const
type Role = keyof typeof portals

function BackHome() {
  return <Link className="back-link" to="/"><ArrowLeft size={16} /> Back to EMBM eFiscal</Link>
}

export function BirSignIn() {
  return <main className="access-page access-bir" id="main-content">
    <div className="access-wrap sign-in-wrap">
      <Brand /><BackHome />
      <Card className="sign-in-card">
        <span className="icon-tile"><ShieldCheck size={23} /></span>
        <h1>BIR Read-Only Portal</h1>
        <p>Regulator access requires formal authorization. Sign-in is disabled.</p>
        <fieldset>
          <Field id="bir-user" label="Authorized government email or user ID"><Input id="bir-user" disabled autoComplete="username" /></Field>
          <Field id="bir-password" label="Password"><Input id="bir-password" disabled type="password" autoComplete="current-password" /></Field>
          <Field id="bir-rdo" label="Requested RDO context (optional)"><Input id="bir-rdo" inputMode="numeric" placeholder="Registered RDO code" /></Field>
          <Button type="button" disabled>Sign In <LockKeyhole size={15} /></Button>
        </fieldset>
        <p className="access-footnote">An RDO request never grants access. An authorized identity provider will enforce regulator permissions and multi-factor authentication.</p>
      </Card>
    </div>
  </main>
}

function ApiStatus() {
  const { isPending, isError, isFetching, refetch } = useQuery(healthQuery)
  if (isPending) return <LoadingState label="Checking local API health" />
  if (isError) return <div><ErrorState>Local API unavailable.</ErrorState><Button disabled={isFetching} variant="light" onClick={() => void refetch()}>{isFetching ? 'Checking…' : 'Try again'}</Button></div>
  return <Badge tone="success">Local API healthy</Badge>
}

export function PortalLayout({ role }: { role: Role }) {
  const portal = portals[role]
  return <main className={'portal portal-' + role} id="main-content">
    <aside className="portal-sidebar">
      <Brand /><p>{portal.title}</p>
      <nav aria-label={portal.title}>
        {portal.sections.map(([Icon, label], index) => index === 0
          ? <Link to={portal.url} key={label} aria-current="page"><Icon size={17} />{label}</Link>
          : <span key={label} aria-disabled="true"><Icon size={17} />{label}</span>)}
      </nav>
    </aside>
    <div className="portal-main">
      <header className="portal-header"><Badge>Development preview</Badge><Button variant="light" asChild><Link to={portal.signIn}>Sign In</Link></Button></header>
      <div className="portal-content">
        <h1>{portal.title}</h1>
        {role === 'client' ? <>
          <p>Explore a sample e-Invoicing workspace. Live invoice issuance and company records are currently unavailable.</p>
          <div className="portal-status"><ApiStatus /><Dialog>
            <DialogTrigger asChild><Button variant="light">About this preview</Button></DialogTrigger>
            <DialogContent title="About this preview" description="The dashboard uses sample records. The API badge checks the local service’s health.">
              <p>Production accounts, payments, documents, and government submissions are unavailable here.</p>
            </DialogContent>
          </Dialog></div>
          <WorkspacePreview />
        </> : <Card><EmptyState title="Access unavailable">{role === 'bir'
          ? 'Regulator access requires formal authorization. The portal is read-only and records will be restricted by grants.'
          : 'Administrative access requires an authenticated vendor account.'}</EmptyState></Card>}
      </div>
    </div>
  </main>
}
