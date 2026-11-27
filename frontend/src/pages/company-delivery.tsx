import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { FileText, Mail, PanelTop } from 'lucide-react'
import { Button, Badge, Card, ErrorState, Field, Input, LoadingState, Select, Table } from '@/components/ui'
import { EmailSampleCard, EmailTemplateFields, LayoutSampleCard, LayoutTemplateFields } from '@/components/company-template-fields'
import { api, errorMessage, manilaTimestamp } from '@/lib/account-api'
import type { DocumentType, EmailSample, EmailTemplate, PdfLayout, SenderData, SenderProfile, TemplateContent, TemplateDetails, TemplateInfo, TemplateKind, TemplateList } from '@/lib/company-configuration'
import './company-delivery.css'

const emptySender: SenderProfile = {
  senderName: '', senderEmail: '', replyTo: null, provider: 'platform', smtpHost: null, smtpPort: null, tlsMode: null, authentication: null, cc: [], bcc: [],
}
const labels = { invoice: 'Sales Invoice', 'credit-memo': 'Credit Memo' }

function useOperation() {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [status, setStatus] = useState('')
  async function run(action: () => Promise<void>) {
    setBusy(true)
    setError('')
    setStatus('')
    try { await action() }
    catch (failure) { setError(errorMessage(failure)) }
    finally { setBusy(false) }
  }
  return { busy, error, status, setError, setStatus, run }
}

function SenderSettings({ base, input, refetch }: { base: string; input: SenderData; refetch: () => Promise<unknown> }) {
  const [profile, setProfile] = useState(input.profile ?? emptySender)
  const [cc, setCc] = useState(profile.cc.join(', '))
  const [bcc, setBcc] = useState(profile.bcc.join(', '))
  const [revision, setRevision] = useState(input.revision)
  const operation = useOperation()
  const custom = profile.provider === 'custom-smtp'
  const copies = (csv: string) => csv.split(',').map(item => item.trim()).filter(Boolean)
  function changeProvider(provider: string) {
    setProfile({ ...profile, provider, smtpHost: provider === 'custom-smtp' ? '' : null,
      smtpPort: provider === 'custom-smtp' ? 587 : null, tlsMode: provider === 'custom-smtp' ? 'starttls' : null,
      authentication: provider === 'custom-smtp' ? 'oauth2' : null })
  }
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const preferences = { ...profile, cc: copies(cc), bcc: copies(bcc) }
    const validate = (event.nativeEvent as SubmitEvent).submitter?.getAttribute('value') === 'validate'
    void operation.run(async () => {
      if (validate) {
        await api(base + '/sender/validate', 'POST', preferences)
        operation.setStatus('Settings are valid. Sender verification and live connectivity are pending.')
      } else {
        const result = await api<{ revision: number }>(base + '/sender', 'PUT', { expectedRevision: revision, profile: preferences })
        setRevision(result.revision)
        await refetch()
        operation.setStatus('Sender settings saved. Provider authorization is pending.')
      }
    })
  }
  return <>
    <Card className="workspace-card"><h2>Email Sender</h2><Badge tone="warning">Pending provider authorization</Badge>
      <p>Save sender preferences for the selected company. Credentials, identity verification, and live connections become available with provider activation.</p>
      <dl className="workspace-definitions">
        <div><dt>Sender verification</dt><dd>{input.senderVerified ? 'Verified' : 'Pending'}</dd></div>
        <div><dt>Connection status</dt><dd>{input.connectionStatus === 'PendingProviderAuthorization' ? 'Pending provider authorization' : input.connectionStatus}</dd></div>
        <div><dt>Last successful local test</dt><dd>{input.lastSuccessfulTestAt ? manilaTimestamp.format(new Date(input.lastSuccessfulTestAt)) : '—'}</dd></div>
      </dl>
      <form onSubmit={submit}><fieldset className="workspace-form" disabled={operation.busy}>
        <div className="form-grid">
          <Field id="sender-name" label="Sender name"><Input id="sender-name" value={profile.senderName} maxLength={100} required
            onChange={e => setProfile({ ...profile, senderName: e.target.value })} /></Field>
          <Field id="sender-email" label="Sender email"><Input id="sender-email" type="email" value={profile.senderEmail} maxLength={254} required
            onChange={e => setProfile({ ...profile, senderEmail: e.target.value })} /></Field>
          <Field id="sender-reply" label="Reply-to email"><Input id="sender-reply" type="email" value={profile.replyTo ?? ''} maxLength={254}
            onChange={e => setProfile({ ...profile, replyTo: e.target.value })} /></Field>
          <Field id="sender-provider" label="Provider preference"><Select id="sender-provider" value={profile.provider} onChange={e => changeProvider(e.target.value)}>
            <option value="platform">Platform managed</option><option value="custom-smtp">Custom SMTP</option>
            <option value="google-workspace">Google Workspace · OAuth preference</option><option value="microsoft-365">Microsoft 365 · OAuth preference</option>
          </Select></Field>
        </div>
        {custom && <div className="form-grid">
          <Field id="smtp-host" label="SMTP hostname"><Input id="smtp-host" value={profile.smtpHost ?? ''} maxLength={253} required
            onChange={e => setProfile({ ...profile, smtpHost: e.target.value })} /></Field>
          <Field id="smtp-port" label="Port and TLS"><Select id="smtp-port" value={profile.smtpPort ?? 587} onChange={e => {
            const port = Number(e.target.value)
            setProfile({ ...profile, smtpPort: port, tlsMode: port === 465 ? 'implicit-tls' : 'starttls' })
          }}><option value={587}>587 · Required STARTTLS</option><option value={465}>465 · Implicit TLS</option></Select></Field>
          <Field id="smtp-auth" label="SMTP authentication preference"><Select id="smtp-auth" value={profile.authentication ?? 'oauth2'}
            onChange={e => setProfile({ ...profile, authentication: e.target.value })}>
            <option value="oauth2">OAuth 2.0</option><option value="password">Password where supported</option>
          </Select></Field>
        </div>}
        <div className="form-grid">
          <Field id="sender-cc" label="Authorized CC addresses"><Input id="sender-cc" value={cc} onChange={e => setCc(e.target.value)} placeholder="Comma separated" /></Field>
          <Field id="sender-bcc" label="Authorized BCC addresses"><Input id="sender-bcc" value={bcc} onChange={e => setBcc(e.target.value)} placeholder="Comma separated" /></Field>
        </div>
        {operation.error && <ErrorState>{operation.error}</ErrorState>}
        {operation.status && <p role="status" className="workspace-success">{operation.status}</p>}
        <div className="access-actions">
          <Button type="submit">Save sender settings</Button>
          <Button type="submit" value="validate" variant="light">Validate settings</Button>
          {input.testMailboxAvailable && <Button type="button" variant="light" disabled={revision === 0}
            onClick={() => void operation.run(async () => {
              await api(base + '/sender/test', 'POST')
              await refetch()
              operation.setStatus('Sender sample written to the development mailbox.')
            })}>Write local sender test</Button>}
        </div>
      </fieldset></form>
    </Card>
    <Card className="workspace-card"><h2>Local email test history</h2>
      <p>Local tests use fictional recipients and write to the development mailbox. Committed document attempts appear in Email Delivery History.</p>
      <Link className="text-link" to="/portal/client/documents/delivery">Open Email Delivery History</Link>
      {input.sampleHistory.length > 0 ? <Table aria-label="Local email test history" className="workspace-table">
        <thead><tr><th scope="col">Date · Manila</th><th scope="col">Status</th></tr></thead>
        <tbody>{input.sampleHistory.map((item, index) => <tr key={item.at + index}><td>{manilaTimestamp.format(new Date(item.at))}</td><td>{item.status}</td></tr>)}</tbody>
      </Table> : <p>No local email tests yet.</p>}
    </Card>
  </>
}

function TemplateEditor({ base, kind, type, list, detail, mailbox, refresh }: {
  base: string; kind: TemplateKind; type: DocumentType; list: TemplateList; detail?: TemplateDetails
  mailbox: boolean; refresh: (selected: string) => Promise<void>
}) {
  const operation = useOperation()
  const [name, setName] = useState(detail?.template.name ?? labels[type] + (kind === 'email' ? ' email' : ' layout'))
  const [content, setContent] = useState<TemplateContent>(detail?.content ?? list.defaultContent)
  const [sample, setSample] = useState<EmailSample>()
  const root = base + '/templates'
  const group = root + '/' + kind + '/' + type
  const template = detail?.template
  const selectedPath = root + '/' + template?.id

  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    void operation.run(async () => {
      const payload = { name, expectedVersion: template?.currentVersion ?? 0, email: kind === 'email' ? content : null, layout: kind === 'pdf' ? content : null }
      const saved = await api<TemplateInfo>(template ? selectedPath : group, template ? 'PUT' : 'POST', payload)
      await refresh(saved.id)
    })
  }
  function preview(test: boolean) {
    void operation.run(async () => {
      const path = base + '/email-preview/' + type
      if (test) {
        await api(path + '/test', 'POST', { email: content })
        operation.setStatus('Test email written to the local mailbox. No message was sent externally.')
      } else {
        setSample(await api<EmailSample>(path, 'POST', { email: content }))
      }
    })
  }

  return <div className="template-designer-grid">
    <div className="workspace-stack">
      <Card className="workspace-card">
        <h2>{kind === 'email' ? 'Email template editor' : 'PDF template layout editor'}</h2>
        <p>{template ? 'Saved version ' + template.currentVersion : 'Start with the built-in template and save a company copy.'}</p>
        <form onSubmit={save}><fieldset className="workspace-form" disabled={operation.busy}>
          <Field id="template-name" label="Template name"><Input id="template-name" maxLength={100} required value={name} onChange={e => setName(e.target.value)} /></Field>
          {kind === 'email'
            ? <EmailTemplateFields value={content as EmailTemplate} update={value => { setContent(value); setSample(undefined) }} variables={list.variables} />
            : <LayoutTemplateFields value={content as PdfLayout} update={setContent} setError={operation.setError} />}
          {operation.error && <ErrorState>{operation.error}</ErrorState>}
          {operation.status && <p role="status" className="workspace-success">{operation.status}</p>}
          <div className="access-actions">
            <Button type="submit">{template ? 'Save new version' : 'Save template'}</Button>
            {kind === 'email' && <Button type="button" variant="light" onClick={() => preview(false)}>Preview email</Button>}
            {kind === 'email' && mailbox && <Button type="button" variant="light" onClick={() => preview(true)}>Write local test email</Button>}
          </div>
        </fieldset></form>
      </Card>
      {template && <Card className="workspace-card"><h2>Saved template</h2>
        <div className="access-actions">
          <Button type="button" variant="light" disabled={operation.busy} onClick={() => void operation.run(async () => {
            await api(group + '/default', 'PUT', { templateId: template.isDefault ? null : template.id })
            await refresh(template.id)
          })}>{template.isDefault ? 'Use built-in default instead' : 'Set as company default'}</Button>
          <Button type="button" variant="light" disabled={operation.busy} onClick={() => void operation.run(async () => {
            const copy = await api<TemplateInfo>(selectedPath + '/duplicate', 'POST', { name: template.name.slice(0, 93) + ' (copy)' })
            await refresh(copy.id)
          })}>Duplicate saved version</Button>
        </div>
        <Table aria-label="Template version history" className="workspace-table">
          <thead><tr><th scope="col">Version</th><th scope="col">Date · Manila</th><th scope="col">Action</th></tr></thead>
          <tbody>{detail.history.map(version => <tr key={version.version}><td>{version.version} · {version.name}</td>
            <td>{manilaTimestamp.format(new Date(version.createdAt))}</td>
            <td>{version.version === template.currentVersion ? 'Current' :
              <button className="text-link" type="button" disabled={operation.busy} onClick={() => void operation.run(async () => {
                await api(selectedPath + '/restore', 'POST', { version: version.version, expectedVersion: template.currentVersion })
                await refresh(template.id)
              })}>Restore version {version.version}</button>}</td></tr>)}</tbody>
        </Table>
      </Card>}
    </div>
    {kind === 'email' ? sample ? <EmailSampleCard sample={sample} /> : <Card className="workspace-card">
      <h2>Email preview</h2><p>Render the subject, body, and sender branding using fictional data.</p>
    </Card> : <LayoutSampleCard value={content as PdfLayout} type={type} base={base} />}
  </div>
}

function Templates({ base, kind, mailbox, initialType }: { base: string; kind: TemplateKind; mailbox: boolean; initialType?: string }) {
  const [type, setType] = useState<DocumentType>(initialType === 'credit-memo' ? 'credit-memo' : 'invoice')
  const [selected, setSelected] = useState('')
  const queryClient = useQueryClient()
  const root = ['delivery-templates', base] as const
  const list = useQuery({ queryKey: [...root, kind, type], queryFn: () => api<TemplateList>(base + '/templates/' + kind + '/' + type) })
  const detail = useQuery({ queryKey: [...root, selected], queryFn: () => api<TemplateDetails>(base + '/templates/' + selected), enabled: !!selected })
  async function refresh(selection: string) {
    setSelected(selection)
    await queryClient.invalidateQueries({ queryKey: root })
  }
  return <>
    <Card className="workspace-card"><h2>{kind === 'email' ? 'Email Templates' : 'PDF Templates'}</h2>
      <div className="form-grid">
        <Field id="template-type" label="Document type"><Select id="template-type" value={type} onChange={event => {
          setSelected(''); setType(event.target.value as DocumentType)
        }}><option value="invoice">Sales Invoice</option><option value="credit-memo">Credit Memo</option></Select></Field>
        <Field id="template-selection" label="Template"><Select id="template-selection" value={selected} onChange={e => setSelected(e.target.value)}>
          <option value="">New from built-in template</option>
          {list.data?.templates.map(item => <option value={item.id} key={item.id}>{item.name}{item.isDefault ? ' · Company default' : ''}</option>)}
        </Select></Field>
      </div>
      <p>Defaults are scoped to this company and document type. Restoring an older version saves it as a new version.</p>
    </Card>
    {list.isPending || (selected && detail.isPending) ? <LoadingState label="Loading templates…" /> :
      list.isError || (selected && detail.isError) ? <ErrorState>{errorMessage(list.error ?? detail.error)}</ErrorState> :
      <TemplateEditor key={kind + type + selected + (detail.data?.template.currentVersion ?? 0)} base={base} kind={kind} type={type}
        list={list.data!} detail={selected ? detail.data : undefined} mailbox={mailbox} refresh={refresh} />}
  </>
}

export function CompanyDeliverySettings({ companyPath, initialSection, initialType }: { companyPath: string; initialSection?: string; initialType?: string }) {
  const [section, setSection] = useState<'sender' | TemplateKind>(initialSection === 'pdf' || initialSection === 'email' ? initialSection : 'sender')
  const base = companyPath + '/configuration'
  const sender = useQuery({ queryKey: ['company-sender', base], queryFn: () => api<SenderData>(base + '/sender') })
  return <>
    <div className="company-delivery-tabs" role="group" aria-label="Delivery and template settings">
      {([['sender', 'Email Sender', Mail], ['email', 'Email Templates', PanelTop], ['pdf', 'PDF Templates', FileText]] as const).map(([key, label, Icon]) =>
        <Button type="button" key={key} variant={section === key ? 'primary' : 'light'} aria-pressed={section === key}
          onClick={() => setSection(key)}><Icon size={16} />{label}</Button>)}
    </div>
    {sender.isPending ? <LoadingState /> : sender.isError ? <ErrorState>{errorMessage(sender.error)}</ErrorState> : section === 'sender'
      ? <SenderSettings base={base} input={sender.data} refetch={sender.refetch} />
      : <Templates key={section} base={base} kind={section} mailbox={sender.data.testMailboxAvailable} initialType={initialType} />}
  </>
}
