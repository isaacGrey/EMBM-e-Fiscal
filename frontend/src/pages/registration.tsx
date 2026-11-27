import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, ArrowRight, CircleCheck } from 'lucide-react'
import { useForm, useWatch } from 'react-hook-form'
import { Link, useSearchParams } from 'react-router-dom'
import { z } from 'zod'
import { PageIntro } from '@/components/public-content'
import { api, ApiError, errorMessage } from '@/lib/account-api'
import { PasswordInput } from '@/components/password-input'
import { Badge, Button, Card, Dialog, DialogContent, DialogTrigger, ErrorState, Field, Input, LoadingState, Money, Select } from '@/components/ui'
import { billingPeriods, catalogQuery, formatCount, isBilling, pricingUrl, type Billing } from '@/lib/subscriptions'

const registrationSchema = z.object({
  classification: z.enum(['b2b', 'b2c']),
  fullName: z.string().trim().min(1, 'Enter a full name.').max(100),
  businessName: z.string().trim().max(120).optional(),
  email: z.string().trim().max(254).email('Enter a valid email address.'),
  mobile: z.string().trim().regex(/^(?:\+63|0)9\d{9}$/, 'Enter a Philippine mobile number such as 09171234567 or +639171234567.'),
  password: z.string().min(12, 'Use at least 12 characters.').max(128, 'Use at most 128 characters.'),
  confirmation: z.string().min(1, 'Confirm your sample password.'),
  termsAccepted: z.boolean().refine(Boolean, 'Acknowledge the terms.'),
  privacyAccepted: z.boolean().refine(Boolean, 'Acknowledge the privacy notice.'),
}).superRefine((values, context) => {
  if (values.classification === 'b2b' && !values.businessName) {
    context.addIssue({ code: 'custom', path: ['businessName'], message: 'Enter the business name.' })
  }
  if (values.confirmation && values.password !== values.confirmation) {
    context.addIssue({ code: 'custom', path: ['confirmation'], message: 'The passwords must match.' })
  }
})

type RegistrationValues = z.infer<typeof registrationSchema>

function RegistrationForm({ planId, billing }: { planId: string; billing: Billing }) {
  const [complete, setComplete] = useState(false)
  const [failure, setFailure] = useState('')
  const { register, control, handleSubmit, reset, setError, formState: { errors, isSubmitting } } = useForm<RegistrationValues>({
    resolver: zodResolver(registrationSchema), defaultValues: { classification: 'b2b', termsAccepted: false, privacyAccepted: false },
    shouldUnregister: true, mode: 'onTouched',
  })
  const classification = useWatch({ control, name: 'classification' })

  function fieldProps(name: keyof RegistrationValues, hint?: string) {
    const id = 'registration-' + name
    const error = errors[name]?.message
    return {
      field: { id, error },
      input: { id, 'aria-invalid': !!error, 'aria-describedby': [hint, error && id + '-error'].filter(Boolean).join(' ') || undefined },
    }
  }

  const fullName = fieldProps('fullName')
  const businessName = fieldProps('businessName')
  const email = fieldProps('email')
  const mobile = fieldProps('mobile')
  const password = fieldProps('password', 'sample-password-rules')
  const confirmation = fieldProps('confirmation')
  const terms = fieldProps('termsAccepted')
  const privacy = fieldProps('privacyAccepted')

  if (complete) return <Card className="registration-card registration-success">
    <div role="status"><CircleCheck size={30} /><h2>Check your email</h2>
      <p>If eligible, you’ll receive a verification link. Verify your email, then sign in to view your selected subscription and payment status.</p>
      {import.meta.env.DEV && <p>Local development uses a simulated mailbox. Use test details.</p>}</div>
    <div className="identity-links"><Link className="text-link" to="/resend-verification">Resend verification</Link>
      <Link className="text-link" to="/sign-in">Sign In</Link></div>
  </Card>

  return <Card className="registration-card">
    <h2>Subscriber details</h2>
    <p>Verify your email to continue. After verified payment and provisioning, the purchasing subscriber becomes the Account Owner.</p>
    {import.meta.env.DEV && <p>Local development uses test accounts and a simulated mailbox.</p>}
    <form noValidate onSubmit={handleSubmit(async values => {
      setFailure('')
      try {
        await api('/auth/register', 'POST', { ...values, planId, billing })
        reset()
        setComplete(true)
      } catch (error) {
        if (error instanceof ApiError && Object.keys(error.fields).length) {
          for (const [field, messages] of Object.entries(error.fields)) setError(field as keyof RegistrationValues, { message: messages[0] })
        } else setFailure(errorMessage(error))
      }
    })}>
      <fieldset disabled={isSubmitting} className="registration-fields">
        <Field id="registration-classification" label="Subscriber classification">
          <Select id="registration-classification" {...register('classification')}>
            <option value="b2b">Corporate / Business Subscriber — B2B</option>
            <option value="b2c">Individual Subscriber — B2C</option>
          </Select>
        </Field>
        <div className="form-grid">
          <Field {...fullName.field} label="Full name"><Input {...fullName.input} maxLength={100} required {...register('fullName')} /></Field>
          {classification === 'b2b' && <Field {...businessName.field} label="Business name">
            <Input {...businessName.input} maxLength={120} required {...register('businessName')} />
          </Field>}
          <Field {...email.field} label="Email address"><Input {...email.input} type="email" autoComplete="email" maxLength={254} required {...register('email')} /></Field>
          <Field {...mobile.field} label="Mobile number"><Input {...mobile.input} type="tel" maxLength={13} required {...register('mobile')} /></Field>
          <Field {...password.field} label="Password"><PasswordInput {...password.input} autoComplete="new-password" maxLength={128} required
            nameForToggle="password" {...register('password')} /></Field>
          <Field {...confirmation.field} label="Confirm password"><PasswordInput {...confirmation.input} autoComplete="new-password" maxLength={128} required
            nameForToggle="password confirmation" {...register('confirmation')} /></Field>
        </div>
        <p className="content-note" id="sample-password-rules">Use 12 to 128 characters.</p>
        <Dialog>
          <DialogTrigger asChild><button type="button" className="text-link">Read development terms and privacy notice</button></DialogTrigger>
          <DialogContent title="Development terms and privacy" description="This environment creates a local identity for email verification and subscription selection.">
            <p>Use test details in development. The API stores your identity and plan selection to support verification. Paid provisioning requires verified payment. Production terms and privacy documents are pending owner approval.</p>
          </DialogContent>
        </Dialog>
        <div className="acknowledgement">
          <input type="checkbox" {...terms.input} required {...register('termsAccepted')} />
          <label htmlFor={terms.field.id}>I acknowledge the development terms.</label>
        </div>
        {terms.field.error && <p id={terms.field.id + '-error'} className="field-error" role="alert">{terms.field.error}</p>}
        <div className="acknowledgement">
          <input type="checkbox" {...privacy.input} required {...register('privacyAccepted')} />
          <label htmlFor={privacy.field.id}>I acknowledge the development privacy notice.</label>
        </div>
        {privacy.field.error && <p id={privacy.field.id + '-error'} className="field-error" role="alert">{privacy.field.error}</p>}
        {failure && <ErrorState>{failure}</ErrorState>}
        <Button type="submit" disabled={isSubmitting}>Create Account <ArrowRight size={16} /></Button>
      </fieldset>
      {isSubmitting && <LoadingState label="Creating your account…" />}
    </form>
  </Card>
}

export function GetStarted() {
  const [params] = useSearchParams()
  const planId = params.get('plan')
  const requested = params.get('billing')
  const billing: Billing = isBilling(requested) ? requested : 'monthly'
  const catalog = useQuery(catalogQuery)
  const selected = catalog.data?.plans.find(plan => plan.id === planId)
  const offer = selected?.billing[billing]
  const invalid = (planId !== null && !selected) || (requested !== null && !isBilling(requested))
  return <main id="main-content">
    <PageIntro label="SUBSCRIPTION REGISTRATION" title="Get started with EMBM eFiscal">
      Choose a subscription and verify your email to continue.
    </PageIntro>
    <section className="section section-tint"><div className="container">
      <Link className="text-link section-back" to={pricingUrl(billing)}><ArrowLeft size={16} /> Back to Pricing</Link>
      {planId && catalog.isPending ? <LoadingState label="Loading your subscription…" /> : planId && catalog.isError
        ? <ErrorState>{errorMessage(catalog.error)}</ErrorState> : selected && offer && !invalid ? <div className="registration-layout">
        <Card className="registration-summary">
          <Badge tone="warning">Illustrative plan</Badge><h2>{selected.name}</h2>
          <div className="subscription-price"><strong><Money amount={offer.price} /></strong><span> / {billingPeriods[billing].unit}</span></div>
          <dl className="subscription-limits">
            <div><dt>Billing frequency</dt><dd>{billingPeriods[billing].label}</dd></div>
            <div><dt>Included Credits</dt><dd>{formatCount(offer.credits)}</dd></div>
            <div><dt>Company files</dt><dd>{formatCount(selected.companyFiles)}</dd></div>
            <div><dt>Authorized users</dt><dd>{formatCount(selected.users)}</dd></div>
          </dl>
          <p>Included Credits are for successful Save &amp; Send commitments. Sales Order saving uses zero credits.</p>
          <p>Company TIN, RDO, taxpayer information, files, and branches are collected during company onboarding.</p>
          <p>Checkout follows email verification. The server activates the subscription after confirming payment.</p>
        </Card>
        <RegistrationForm key={selected.id + billing} planId={selected.id} billing={billing} />
      </div> : <Card className="registration-card choose-plan">
        <h2>Choose a subscription</h2>
        {invalid && <ErrorState>This plan or billing option is unavailable. Choose an option from Pricing.</ErrorState>}
        <p>Compare monthly and annual plans, or explore the sample e-Invoicing workspace.</p>
        <div className="access-actions">
          <Button asChild><Link to={pricingUrl(billing)}>Browse pricing <ArrowRight size={16} /></Link></Button>
          <Button variant="light" asChild><Link to="/preview/client">Explore sample workspace</Link></Button>
        </div>
      </Card>}
    </div></section>
  </main>
}
