import { VendorBilling } from './vendor-billing'
import { VendorConnect } from './vendor-connect'
import { VendorAccreditationWorkspace } from './vendor-accreditation'
import { useState, type FormEvent, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, ArrowRight, LockKeyhole } from 'lucide-react'
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { Brand } from '@/components/brand'
import { PasswordInput } from '@/components/password-input'
import { Badge, Button, Card, ErrorState, Field, Input, LoadingState } from '@/components/ui'
import { api, errorMessage, sessionQuery } from '@/lib/account-api'

export function AccountShell({ title, description, children, wide }: { title: string; description: string; children: ReactNode; wide?: boolean }) {
  return <main className="access-page" id="main-content"><div className={wide ? 'access-wrap vendor-billing-wrap' : 'access-wrap sign-in-wrap'}>
    <Brand /><Link className="back-link" to="/"><ArrowLeft size={16} /> Back to EMBM eFiscal</Link>
    <Card className="sign-in-card"><span className="icon-tile"><LockKeyhole size={22} /></span><h1>{title}</h1><p>{description}</p>{children}</Card>
  </div></main>
}

function useAccountAction() {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [done, setDone] = useState(false)
  async function run(action: () => Promise<unknown>) {
    setBusy(true)
    setError('')
    try { await action(); setDone(true) }
    catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  return { busy, error, done, run }
}

function useEmailToken() {
  const { hash } = useLocation()
  return new URLSearchParams(hash.replace(/^#/, '')).get('token') ?? ''
}

export function IdentitySignIn({ kind }: { kind: 'client' | 'vendor' }) {
  const vendor = kind === 'vendor'
  const action = useAccountAction()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [params] = useSearchParams()
  const token = useEmailToken()
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    void action.run(async () => {
      await api('/auth/' + (vendor ? 'vendor/' : '') + 'login', 'POST', {
        email: form.get('email'), password: form.get('password'), rememberMe: form.has('remember'),
      })
      queryClient.clear()
      navigate(vendor ? '/portal/vendor' : params.get('return') === 'invitation' && token
        ? '/invitation#token=' + encodeURIComponent(token) : '/portal/client', { replace: true })
    })
  }
  return <AccountShell title={vendor ? 'Vendor Super Admin Portal' : 'Client Business Portal'}
    description={vendor ? 'For authorized administrators. Production access requires multi-factor authentication.' : 'Sign in to your EMBM eFiscal account.'}>
    <form onSubmit={submit}><fieldset disabled={action.busy}>
      <Field id="sign-in-email" label={vendor ? 'Administrator email' : 'Email address'}><Input id="sign-in-email" name="email" type="email" maxLength={254} autoComplete="username" required /></Field>
      <Field id="sign-in-password" label="Password"><PasswordInput id="sign-in-password" name="password" nameForToggle="password" autoComplete="current-password" maxLength={128} required /></Field>
      {!vendor && <label className="acknowledgement"><input type="checkbox" name="remember" /> Remember Me</label>}
      {action.error && <ErrorState>{action.error}</ErrorState>}
      <Button type="submit">{action.busy ? 'Signing in…' : 'Sign In'} <ArrowRight size={16} /></Button>
    </fieldset></form>
    {!vendor && <div className="identity-links">
      <Link className="text-link" to="/forgot-password">Forgot Password</Link>
      <Link className="text-link" to="/pricing">Create Account</Link>
      <Link className="text-link" to="/preview/client">Explore the sample workspace</Link>
    </div>}
  </AccountShell>
}

export function RecoverAccount({ resend = false }: { resend?: boolean }) {
  const action = useAccountAction()
  const [params] = useSearchParams()
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    void action.run(() => api(resend ? '/auth/resend-verification' : '/auth/forgot-password', 'POST', { email: form.get('email') }))
  }
  return <AccountShell title={resend ? 'Resend verification' : 'Forgot Password'}
    description={resend ? 'Request another link to verify your email.' : 'Request a link to reset your password.'}>
    {action.done ? <p className="identity-result" role="status">If eligible, an email will arrive shortly.{import.meta.env.DEV && ' Local development uses the simulated mailbox.'}</p>
      : <form onSubmit={submit}><fieldset disabled={action.busy}>
        <Field id="recovery-email" label="Email address"><Input id="recovery-email" name="email" type="email" maxLength={254} required autoComplete="email" defaultValue={params.get('email') ?? ''} /></Field>
        {action.error && <ErrorState>{action.error}</ErrorState>}
        <Button type="submit">{action.busy ? 'Sending…' : 'Send email'}</Button>
      </fieldset></form>}
    <Link className="text-link section-link" to="/sign-in">Back to Sign In</Link>
  </AccountShell>
}

export function VerifyEmail() {
  const token = useEmailToken()
  const action = useAccountAction()
  return <AccountShell title={action.done ? 'Email verified' : 'Verify your email'}
    description={action.done ? 'Sign in to view your subscription and payment status.' : 'Confirm your email address to continue with your subscription.'}>
    {action.done ? <Button type="button" asChild className="section-link"><Link to="/sign-in">Sign In</Link></Button>
      : <div className="identity-links">
        {(!token || action.error) && <ErrorState>{action.error || 'This link is invalid or expired.'}</ErrorState>}
        {token && <Button type="button" disabled={action.busy} onClick={() => void action.run(() => api('/auth/verify-email', 'POST', { token }))}>
          {action.busy ? 'Verifying…' : 'Verify email'}
        </Button>}
        <Link className="text-link" to="/resend-verification">Request another verification link</Link>
      </div>}
  </AccountShell>
}

export function ResetPassword() {
  const token = useEmailToken()
  const action = useAccountAction()
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    void action.run(() => api('/auth/reset-password', 'POST', { token, password: form.get('password'), confirmation: form.get('confirmation') }))
  }
  return <AccountShell title={action.done ? 'Password reset' : 'Reset Password'}
    description={action.done ? 'Your previous sessions have ended. Sign in with your new password.' : 'Use 12 to 128 characters and confirm your new password.'}>
    {action.done ? <Link className="text-link section-link" to="/sign-in">Sign In</Link> : <>
      {!token && <ErrorState>This link is invalid or expired.</ErrorState>}
      {token && <form onSubmit={submit}><fieldset disabled={action.busy}>
        <NewPasswordFields prefix="reset" />
        {action.error && <ErrorState>{action.error}</ErrorState>}
        <Button type="submit">{action.busy ? 'Resetting…' : 'Reset password'}</Button>
      </fieldset></form>}
      <Link className="text-link section-link" to="/forgot-password">Request a new reset link</Link>
    </>}
  </AccountShell>
}

export function NewPasswordFields({ prefix }: { prefix: string }) {
  return <>
    <Field id={prefix + '-password'} label="New password"><PasswordInput id={prefix + '-password'} name="password" nameForToggle="new password" minLength={12} maxLength={128} autoComplete="new-password" required /></Field>
    <Field id={prefix + '-confirmation'} label="Confirm new password"><PasswordInput id={prefix + '-confirmation'} name="confirmation" nameForToggle="password confirmation" minLength={12} maxLength={128} autoComplete="new-password" required /></Field>
  </>
}

export function Invitation() {
  const token = useEmailToken()
  const action = useAccountAction()
  const user = useQuery(sessionQuery)
  const signedIn = user.data?.kind === 'Customer'
  function register(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    void action.run(() => api('/invitations/register', 'POST', {
      token, fullName: form.get('fullName'), mobile: form.get('mobile'), password: form.get('password'), confirmation: form.get('confirmation'),
      termsAccepted: form.has('terms'), privacyAccepted: form.has('privacy'),
    }))
  }
  return <AccountShell title={action.done ? 'Invitation accepted' : 'Join an eFiscal subscriber'}
    description={action.done ? 'Your company access is available from the Client Business Portal.' : 'Use the account for your invited email address, or create it here if you are new.'}>
    {!token && <ErrorState>This invitation is invalid or expired.</ErrorState>}
    {action.error && <ErrorState>{action.error}</ErrorState>}
    {user.isPending && <LoadingState />}
    {action.done ? <Link className="text-link section-link" to={signedIn ? '/portal/client' : '/sign-in'}>{signedIn ? 'Open Client Portal' : 'Sign In'}</Link>
      : token && signedIn ? <Button className="section-link" type="button" disabled={action.busy} onClick={() => void action.run(() => api('/invitations/accept', 'POST', { token }))}>Accept invitation</Button>
      : token && !user.isPending && <>
        <Link className="text-link section-link" to={'/sign-in?return=invitation#token=' + encodeURIComponent(token)}>Sign In to accept</Link>
        <form onSubmit={register}><fieldset disabled={action.busy}>
          <Field id="invitation-name" label="Full name"><Input id="invitation-name" name="fullName" maxLength={100} required /></Field>
          <Field id="invitation-mobile" label="Mobile number"><Input id="invitation-mobile" name="mobile" type="tel" required maxLength={13} /></Field>
          <NewPasswordFields prefix="invite" />
          <label className="acknowledgement"><input type="checkbox" name="terms" required /> I acknowledge the development terms.</label>
          <label className="acknowledgement"><input type="checkbox" name="privacy" required /> I acknowledge the development privacy notice.</label>
          <Button type="submit">{action.busy ? 'Creating…' : 'Create account and accept'}</Button>
        </fieldset></form>
      </>}
  </AccountShell>
}

export function VendorWorkspace() {
  const path = useLocation().pathname
  const builder = path.startsWith('/portal/vendor/connectors')
  const accreditation = path === '/portal/vendor/accreditation'
  const user = useQuery(sessionQuery)
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const action = useAccountAction()
  return <AccountShell title="Vendor Super Admin Portal" description="Local administrator session" wide={user.data?.kind === 'Vendor'}>
    {user.isPending ? <LoadingState /> : user.data?.kind !== 'Vendor'
      ? <Link className="text-link section-link" to="/vendor/sign-in">Sign In</Link>
      : <div className={builder ? "identity-links connect-builder" : "identity-links"}><Badge>Local development</Badge>
        <p>Signed in as {user.data.fullName}.</p>
        {action.error && <ErrorState>{action.error}</ErrorState>}
        <Button type="button" disabled={action.busy} onClick={() => void action.run(async () => {
          await api('/auth/logout', 'POST')
          queryClient.setQueryData(sessionQuery.queryKey, null)
          navigate('/vendor/sign-in', { replace: true })
        })}>Sign Out</Button>
        <nav aria-label="Vendor workspaces" className="connect-actions">
          <Link className="text-link" to="/portal/vendor">Commercial administration</Link>
          <Link className="text-link" to="/portal/vendor/connectors">Universal API Connector Builder</Link>
          <Link className="text-link" to="/portal/vendor/accreditation">Regulatory Compliance</Link>
          {builder && <>
            <Link className="text-link" to="/portal/vendor/connectors/api-sandbox">API sandbox</Link>
            <Link className="text-link" to="/portal/vendor/connectors/mapping-templates">Mapping templates</Link>
            <Link className="text-link" to="/portal/vendor/connectors/monitor">Connector monitoring</Link>
          </>}
        </nav>
        {builder ? <VendorConnect /> : accreditation ? <VendorAccreditationWorkspace /> : <VendorBilling />}
      </div>}
  </AccountShell>
}
