import { useState, type FormEvent, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { BranchFields, CompanyFields, emptyBranch, TaxpayerFields } from '@/components/company-forms'
import { Badge, Button, Card, ErrorState, Field, Input, LoadingState, Select, Table } from '@/components/ui'
import { api, errorMessage, manilaDate, manilaTimestamp, type Branch, type BranchInput, type CompanyDetails, type CompanyList, type RdoReference, type Session, type TeamData } from '@/lib/account-api'
import { NewPasswordFields } from './identity'
import { PasswordInput } from '@/components/password-input'

export function SettingsForm({ title, action, submit, children }: { title: string; action: string; submit: (data: FormData) => Promise<unknown>; children: ReactNode }) {
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState('')
  async function handle(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setSaved(false)
    setError('')
    const data = new FormData(event.currentTarget)
    try { await submit(data); setSaved(true) }
    catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  return <Card className="workspace-card"><h2>{title}</h2>
    <form onSubmit={event => void handle(event)}><fieldset className="workspace-form" disabled={busy}>
      {children}
      {error && <ErrorState>{error}</ErrorState>}
      {saved && <p className="workspace-success" role="status">Saved.</p>}
      <Button type="submit">{busy ? 'Saving…' : action}</Button>
    </fieldset></form>
  </Card>
}

type ConfigProps = { path: string; details: CompanyDetails; saved: () => Promise<void> }

export function CompanySettings({ path, details, saved }: ConfigProps) {
  const [company, setCompany] = useState(details.company)
  return <SettingsForm title="Company Information" action="Save company information" submit={async () => {
    await api(path, 'PUT', company); await saved()
  }}><CompanyFields value={company} change={setCompany} /></SettingsForm>
}

export function TaxpayerSettings({ path, details, rdos, saved }: ConfigProps & { rdos: RdoReference[] }) {
  const assignment = details.assignments.find(item => !item.branchId && !item.effectiveUntil)
  const [taxpayer, setTaxpayer] = useState({ ...details.taxpayer, rdoCode: assignment?.code ?? '', effectiveFrom: manilaDate() })
  return <>
    <SettingsForm title="Taxpayer Registration" action="Save taxpayer registration" submit={async () => {
      const { verificationStatus: _verification, issuanceStatus: _issuance, ...editable } = taxpayer
      await api(path + '/taxpayer', 'PUT', editable); await saved()
    }}><TaxpayerFields value={taxpayer} change={input => setTaxpayer({ ...taxpayer, ...input })} rdos={rdos} /></SettingsForm>
    <SettingsForm title="Registration evidence" action="Attach PDF evidence" submit={async form => {
      await api(path + '/evidence', 'POST', form); await saved()
    }}>
      <Field id="registration-evidence" label="Registration Evidence PDF"><Input type="file" accept=".pdf,application/pdf" id="registration-evidence" name="file" required /></Field>
      <p className="content-note">PDF files up to 2 MB are stored privately. An attachment does not verify a taxpayer.</p>
      {details.evidence.length > 0 && <ul>{details.evidence.map(file => <li key={file.id}>{file.name} · {manilaTimestamp.format(new Date(file.uploadedAt))}</li>)}</ul>}
    </SettingsForm>
  </>
}

export function RdoHistory({ details }: { details: CompanyDetails }) {
  return <Card className="workspace-card"><h2>Registered RDO</h2>
    <Table aria-label="Registered RDO assignments" className="workspace-table">
      <thead><tr><th scope="col">RDO</th><th scope="col">Scope</th><th scope="col">Effective from</th><th scope="col">Effective until</th><th scope="col">Registration status</th></tr></thead>
      <tbody>{details.assignments.map(item => <tr key={item.id}><td>{item.code} · {item.name}</td><td>{item.branchId ? 'Branch' : 'Company'}</td>
        <td>{item.effectiveFrom}</td><td>{item.effectiveUntil ?? 'Current'}</td><td>{item.registrationStatus === 'NotVerified' ? 'Not verified' : item.registrationStatus}</td></tr>)}</tbody>
    </Table>
  </Card>
}

function BranchEditor({ current, path, rdos, details, saved }: { current?: Branch; rdos: RdoReference[]; path: string; details: CompanyDetails; saved: () => Promise<void> }) {
  const [branch, setBranch] = useState<BranchInput>(current ? { ...current, rdoCode: details.assignments.find(item => item.branchId === current.id && !item.effectiveUntil)?.code ?? '' }
    : { ...emptyBranch, type: 'Branch' })
  return <SettingsForm title={current ? 'Edit branch' : 'Add branch'} action={current ? 'Save branch' : 'Create branch'} submit={async () => {
    await api(path + '/branches' + (current ? '/' + current.id : ''), current ? 'PUT' : 'POST', branch)
    if (!current) setBranch({ ...emptyBranch, type: 'Branch' })
    await saved()
  }}><BranchFields value={branch} change={setBranch} rdos={rdos} /></SettingsForm>
}

export function BranchSettings(props: ConfigProps & { rdos: RdoReference[]; branches: Branch[]; editable: boolean }) {
  const [picked, setPicked] = useState<string>('')
  const current = props.branches.find(branch => branch.id === picked)
  return <>
    <Card className="workspace-card"><h2>Branch Management</h2>
      <Table className="workspace-table" aria-label="Company branches">
        <thead><tr><th scope="col">Code</th><th scope="col">Registered branch</th><th scope="col">Location type</th>{props.editable && <th scope="col">Action</th>}</tr></thead>
        <tbody>{props.branches.map(branch => <tr key={branch.id}><td>{branch.code}</td><td>{branch.name}</td>
          <td>{branch.type === 'HeadOffice' ? 'Head Office' : 'Branch'}</td>
          {props.editable && <td><button className="text-link" type="button" onClick={() => setPicked(branch.id)}>Edit {branch.code}</button></td>}
        </tr>)}</tbody>
      </Table>
      {props.editable && current && <button className="text-link section-link" type="button" onClick={() => setPicked('')}>Add another branch</button>}
    </Card>
    {props.editable && <BranchEditor key={picked} current={current} {...props} />}
  </>
}

type TeamProps = { path: string; companies: CompanyList['companies'] }
function AccessFields({ roles, companies, role, selected, prefix }: { roles: TeamData['roles']; companies: TeamProps['companies']; role?: string; selected?: string[]; prefix: string }) {
  return <>
    <Field id={prefix + '-role'} label="Assigned Role"><Select id={prefix + '-role'} name="role" defaultValue={role ?? ''} required>
      <option value="">Choose a role</option>{roles.map(r => <option key={r.id} value={r.id}>{r.name}</option>)}
    </Select></Field>
    <fieldset className="company-checks"><legend>Authorized Company Files</legend>
      {companies.map(company => <label className="acknowledgement" key={company.id}>
        <input type="checkbox" name="companies" value={company.id} defaultChecked={selected?.includes(company.id)} /> {company.code} · {company.registeredName}
      </label>)}
    </fieldset>
  </>
}

function MemberEditor({ member, roles, companies, path, reload }: TeamProps & { member: TeamData['members'][number]; roles: TeamData['roles']; reload: () => Promise<unknown> }) {
  const [removing, setRemoving] = useState(false)
  const [error, setError] = useState('')
  const endpoint = path + '/members/' + member.userId
  return <div className="workspace-stack">
    <SettingsForm title={member.fullName} action="Save member access" submit={async form => {
      await api(endpoint, 'PUT', { roleId: form.get('role'), companyIds: form.getAll('companies') }); await reload()
    }}>
      <p>{member.email}</p>
      <AccessFields companies={companies} roles={roles} prefix={member.userId} role={member.roleId} selected={member.companyIds} />
    </SettingsForm>
    {removing ? <div className="workspace-stack"><p>Remove access to this subscriber?</p>
      {error && <ErrorState>{error}</ErrorState>}
      <div className="access-actions"><Button type="button" variant="light" onClick={() => setRemoving(false)}>Keep access</Button>
        <Button type="button" onClick={() => void api(endpoint, 'DELETE').then(reload).catch(failure => setError(errorMessage(failure)))}>Remove access</Button></div>
    </div> : <button className="text-link" type="button" onClick={() => setRemoving(true)}>Remove {member.fullName}</button>}
  </div>
}

export function TeamSettings({ path, companies }: TeamProps) {
  const team = useQuery({ queryKey: ['team', path], queryFn: () => api<TeamData>(path) })
  const [memberId, setMemberId] = useState('')
  if (team.isPending) return <LoadingState />
  if (team.isError) return <ErrorState>{errorMessage(team.error)}</ErrorState>
  const data = team.data
  const selected = data.members.find(member => member.userId === memberId && member.roleId !== 'owner')
  return <>
    <SettingsForm title="Team Members" action="Send invitation" submit={async form => {
      await api(path + '/invitations', 'POST', { email: form.get('email'), roleId: form.get('role'), companyIds: form.getAll('companies') }); await team.refetch()
    }}>
      <p>Invitations expire after 48 hours and grant access only to the selected company files.</p>
      <Field id="team-email" label="Invite Email"><Input id="team-email" name="email" type="email" required maxLength={254} /></Field>
      <AccessFields companies={companies} roles={data.roles} prefix="invite-access" />
    </SettingsForm>
    <Card className="workspace-card"><h2>Invitations</h2>
      {data.invitations.length === 0 ? <p>No invitations yet.</p> : <Table className="workspace-table" aria-label="Team invitations"><thead>
        <tr><th scope="col">Email</th><th scope="col">Role</th><th scope="col">Status</th><th scope="col">Expires · Manila</th></tr></thead>
        <tbody>{data.invitations.map(invite => <tr key={invite.id}><td>{invite.email}</td><td>{data.roles.find(role => role.id === invite.roleId)?.name ?? invite.roleId}</td>
          <td>{invite.acceptedAt ? 'Accepted' : new Date(invite.expiresAt).getTime() > Date.now() ? 'Pending' : 'Expired'}</td>
          <td>{manilaTimestamp.format(new Date(invite.expiresAt))}</td></tr>)}</tbody></Table>}
    </Card>
    {data.canManage && <Card className="workspace-card"><h2>Manage company access</h2>
      <p>Account Owner changes require a separate secure ownership transfer.</p>
      <ul className="workspace-stack">{data.members.map(member => <li key={member.userId}>
        {member.roleId === 'owner' ? <span>{member.fullName} <Badge>Account Owner</Badge></span> :
          <button type="button" className="text-link" onClick={() => setMemberId(member.userId)}>Manage {member.fullName}</button>}
      </li>)}</ul>
    </Card>}
    {selected && <MemberEditor key={selected.userId} member={selected} roles={data.roles} companies={companies} path={path} reload={team.refetch} />}
  </>
}

export function UserSettings({ user, refresh }: { user: Session; refresh: () => Promise<void> }) {
  return <SettingsForm title="User Profile" action="Save profile" submit={async form => {
    await api('/auth/profile', 'PUT', { fullName: form.get('name'), mobile: form.get('mobile') }); await refresh()
  }}>
    <Field id="profile-name" label="Full name"><Input id="profile-name" name="name" defaultValue={user.fullName} required maxLength={100} /></Field>
    <Field id="profile-mobile" label="Mobile number"><Input id="profile-mobile" name="mobile" type="tel" defaultValue={user.mobile} required maxLength={13} /></Field>
    <p>Email address · {user.email}</p>
  </SettingsForm>
}

export function SecuritySettings({ signedOut }: { signedOut: () => Promise<void> }) {
  return <SettingsForm title="Password and Security" action="Change password and sign out" submit={async form => {
    await api('/auth/password', 'PUT', { currentPassword: form.get('current'), password: form.get('password'), confirmation: form.get('confirmation') })
    await signedOut()
  }}>
    <p>Changing your password ends your active sessions.</p>
    <Field id="current-password" label="Current password"><PasswordInput id="current-password" name="current" nameForToggle="current password" autoComplete="current-password" required maxLength={128} /></Field>
    <NewPasswordFields prefix="change" />
  </SettingsForm>
}
