import { useState, type FormEvent } from 'react'
import { Check, ChevronLeft, ChevronRight } from 'lucide-react'
import { Button, Card, ErrorState, Field, Input, Select } from './ui'
import { api, errorMessage, manilaDate, type BranchInput, type CompanyInput, type RdoReference, type TaxpayerInput } from '@/lib/account-api'

const emptyCompany: CompanyInput = { code: '', registeredName: '', tradeName: '', businessClassification: '', address: '', email: '', phone: '' }
const emptyTaxpayer: TaxpayerInput = { tin: '', vatStatus: '', rdoCode: '', effectiveFrom: '', registrationDetails: '', permitReference: '' }
export const emptyBranch: BranchInput = { type: 'HeadOffice', code: '', name: '', address: '', taxRegistration: '', rdoCode: '', contact: '' }

type Spec<T> = { name: keyof T & string; label: string; maxLength?: number; required?: boolean; type?: string; pattern?: string }
const companyFields: Spec<CompanyInput>[] = [
  { name: 'code', label: 'Company File Code', maxLength: 20, required: true, pattern: '[A-Za-z0-9][A-Za-z0-9\\-]{1,19}' },
  { name: 'registeredName', label: 'Registered Business Name', maxLength: 150, required: true },
  { name: 'tradeName', label: 'Trade Name', maxLength: 150 },
  { name: 'businessClassification', label: 'Business Classification', maxLength: 60, required: true },
  { name: 'address', label: 'Registered Address', maxLength: 300, required: true },
  { name: 'email', label: 'Business Email', maxLength: 254, type: 'email', required: true },
  { name: 'phone', label: 'Contact Number', maxLength: 30, type: 'tel', required: true },
]
const branchFields: Spec<BranchInput>[] = [
  { name: 'code', label: 'Branch Code', maxLength: 20, required: true, pattern: '[A-Za-z0-9][A-Za-z0-9\\-]{1,19}' },
  { name: 'name', label: 'Registered Branch Name', maxLength: 150, required: true },
  { name: 'address', label: 'Branch Address', maxLength: 300, required: true },
  { name: 'taxRegistration', label: 'Branch Tax Registration', maxLength: 120 },
  { name: 'contact', label: 'Branch Contact Information', maxLength: 80 },
]
const taxFields: Spec<TaxpayerInput>[] = [
  { name: 'tin', label: 'Registered TIN', maxLength: 16, required: true, pattern: '[0-9]{3}-?[0-9]{3}-?[0-9]{3}(-?[0-9]{3})?' },
  { name: 'registrationDetails', label: 'Registration Details', maxLength: 500 },
  { name: 'permitReference', label: 'Permit Reference', maxLength: 120 },
]

function Fields<T extends Record<string, string>>({ prefix, specs, value, change }: { prefix: string; specs: Spec<T>[]; value: T; change: (value: T) => void }) {
  return <>{specs.map(({ name, label, ...input }) => <Field key={name} id={prefix + '-' + name} label={label}>
    <Input id={prefix + '-' + name} value={value[name] ?? ''} onChange={event => change({ ...value, [name]: event.target.value })} {...input} />
  </Field>)}</>
}

export function CompanyFields({ value, change }: { value: CompanyInput; change: (value: CompanyInput) => void }) {
  return <div className="form-grid"><Fields prefix="company" specs={companyFields} value={value} change={change} /></div>
}

function RdoSelect({ id, value, change, options, required }: { id: string; value: string; change: (code: string) => void; options: RdoReference[]; required?: boolean }) {
  return <Field id={id} label={required ? 'Registered RDO' : 'Branch Registered RDO (optional)'}>
    <Select id={id} value={value} onChange={event => change(event.target.value)} required={required}>
      <option value="">{required ? 'Choose a registered RDO' : 'No separate branch assignment'}</option>
      {options.map(rdo => <option key={rdo.code} value={rdo.code}>{rdo.code} · {rdo.name}</option>)}
    </Select>
  </Field>
}

export function TaxpayerFields({ value, change, rdos }: { value: TaxpayerInput; change: (value: TaxpayerInput) => void; rdos: RdoReference[] }) {
  return <>
    <div className="form-grid">
      <Fields prefix="taxpayer" specs={taxFields} value={value} change={change} />
      <Field id="vat-status" label="VAT Registration Status"><Select id="vat-status" value={value.vatStatus} required onChange={event => change({ ...value, vatStatus: event.target.value })}>
        <option value="">Choose a status</option><option value="VAT">VAT registered</option><option value="NonVAT">Non-VAT</option><option value="Exempt">Exempt</option>
      </Select></Field>
      <RdoSelect id="registered-rdo" options={rdos} value={value.rdoCode} change={code => change({ ...value, rdoCode: code })} required />
      <Field id="rdo-effective" label="RDO Effective Date"><Input id="rdo-effective" type="date" value={value.effectiveFrom}
        max={manilaDate()} required onChange={event => change({ ...value, effectiveFrom: event.target.value })} /></Field>
    </div>
    {rdos.some(rdo => rdo.developmentFixture) && <p className="content-note">Fictional development offices are included. They are not official BIR reference data.</p>}
    {rdos.length === 0 && <ErrorState>The registered RDO list is empty. Ask your administrator to import reference data.</ErrorState>}
  </>
}

export function BranchFields({ value, change, rdos }: { value: BranchInput; change: (value: BranchInput) => void; rdos: RdoReference[] }) {
  return <div className="form-grid">
    <Field id="branch-type" label="Location Type"><Select id="branch-type" value={value.type} onChange={event => change({ ...value, type: event.target.value })}>
      <option value="HeadOffice">Head Office</option><option value="Branch">Branch</option>
    </Select></Field>
    <Fields prefix="branch" specs={branchFields} value={value} change={change} />
    <RdoSelect id="branch-rdo" options={rdos} value={value.rdoCode} change={code => change({ ...value, rdoCode: code })} />
  </div>
}

const steps = ['Registered Business Information', 'Taxpayer Registration', 'Branch Configuration', 'Review', 'Complete Company Setup']

export function CompanyWizard({ path, rdos, complete }: { path: string; rdos: RdoReference[]; complete: () => Promise<void> }) {
  const [index, setIndex] = useState(0)
  const [company, setCompany] = useState(emptyCompany)
  const [taxpayer, setTaxpayer] = useState({ ...emptyTaxpayer, effectiveFrom: manilaDate() })
  const [branch, setBranch] = useState(emptyBranch)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [evidence, setEvidence] = useState<File>()
  const [createdId, setCreatedId] = useState<string>()
  const review = [
    ['Company file', company.code + ' · ' + company.registeredName],
    ['Contact', company.email + ' · ' + company.phone],
    ['Address', company.address],
    ['Taxpayer', taxpayer.tin + ' · ' + taxpayer.vatStatus],
    ['Registered RDO', taxpayer.rdoCode + ' · ' + (rdos.find(rdo => rdo.code === taxpayer.rdoCode)?.name ?? '')],
    ['Effective date', taxpayer.effectiveFrom],
    ['Registration evidence', evidence?.name ?? 'None attached'],
    ['Branch', branch.code + ' · ' + branch.name],
    ['Branch address', branch.address],
  ]

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (index < steps.length - 1) {
      setIndex(index + 1)
      return
    }
    setBusy(true)
    setError('')
    let id = createdId
    try {
      id ??= (await api<{ id: string }>(path + '/companies', 'POST', { company, taxpayer, branch })).id
      setCreatedId(id)
      if (evidence) {
        const form = new FormData()
        form.set('file', evidence)
        await api(path + '/companies/' + id + '/evidence', 'POST', form)
        setEvidence(undefined)
      }
      await complete()
    } catch (failure) {
      setError((id ? 'The company file was saved. ' : '') + errorMessage(failure))
      setBusy(false)
    }
  }
  return <Card className="workspace-card wizard">
    <ol className="wizard-progress" aria-label="Company setup progress">{steps.map((step, position) =>
      <li key={step} aria-current={position === index ? 'step' : undefined}><span>{position < index ? <Check size={13} /> : position + 1}</span>{step}</li>)}</ol>
    <h2>{steps[index]}</h2>
    <form className="workspace-form" onSubmit={event => void submit(event)}>
      {index === 0 && <CompanyFields value={company} change={setCompany} />}
      {index === 1 && <><TaxpayerFields value={taxpayer} change={setTaxpayer} rdos={rdos} />
        <Field id="onboarding-evidence" label="Registration Evidence PDF (optional)">
          <Input id="onboarding-evidence" type="file" accept=".pdf,application/pdf" onChange={event => setEvidence(event.target.files?.[0])} />
        </Field>
        {evidence && <p className="content-note">Selected · {evidence.name}</p>}
        <p className="content-note">PDF files up to 2 MB can be stored privately. Evidence does not verify a taxpayer.</p></>}
      {index === 2 && <BranchFields value={branch} change={setBranch} rdos={rdos} />}
      {index === 3 && <dl className="workspace-definitions">{review.map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>}
      {index === 4 && <p>Company setup creates the company file and first branch. Issuance stays disabled pending a separate regulatory review. Setup uses no Save &amp; Send Credits.</p>}
      {error && <ErrorState>{error}</ErrorState>}
      {createdId && error && <Field id="replacement-evidence" label="Replace Registration Evidence PDF">
        <Input id="replacement-evidence" type="file" accept=".pdf,application/pdf" onChange={event => setEvidence(event.target.files?.[0])} />
      </Field>}
      <div className="access-actions">
        {index > 0 && !createdId && <Button type="button" variant="light" disabled={busy} onClick={() => { setError(''); setIndex(index - 1) }}><ChevronLeft size={16} /> Back</Button>}
        {createdId && error && <Button type="button" variant="light" onClick={() => void complete()}>Open workspace without attachment</Button>}
        <Button type="submit" disabled={busy || (index === 1 && !rdos.length)}>
          {index === 4 ? busy ? 'Completing setup…' : createdId ? 'Retry attachment' : 'Complete Company Setup' : 'Continue'} <ChevronRight size={16} />
        </Button>
      </div>
    </form>
  </Card>
}
