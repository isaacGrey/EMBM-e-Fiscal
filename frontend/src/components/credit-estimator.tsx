import { useQuery } from '@tanstack/react-query'
import { ArrowRight, Calculator } from 'lucide-react'
import { useForm, useWatch } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { z } from 'zod'
import { billingPeriods, catalogQuery, documentTypes, estimateSubscription, formatCount, registrationUrl, type Billing, type SubscriptionUsage } from '@/lib/subscriptions'
import { Button, Card, Field, Input } from './ui'

const maxUsage = 100_000
const quantity = (minimum: number) => z.number({ error: 'Enter a number.' }).int('Enter a whole number.').min(minimum).max(maxUsage)
const usageSchema = z.object({
  invoices: quantity(0), creditMemos: quantity(0), salesOrders: quantity(0),
  companyFiles: quantity(1), users: quantity(1),
})
const fields = [
  ...documentTypes.map(({ key, label }) => ({ key, label, min: 0 })),
  { key: 'companyFiles', label: 'Company files', min: 1 },
  { key: 'users', label: 'Authorized users', min: 1 },
] as const

export function CreditEstimator({ billing = 'monthly' }: { billing?: Billing }) {
  const { register, control } = useForm<SubscriptionUsage>({
    defaultValues: { invoices: 180, creditMemos: 12, salesOrders: 30, companyFiles: 1, users: 2 },
  })
  const catalog = useQuery(catalogQuery)
  const parsed = usageSchema.safeParse(useWatch({ control }))
  const result = parsed.success ? estimateSubscription(parsed.data, billing, catalog.data?.plans ?? []) : null
  const plan = result?.recommended
  const period = billingPeriods[billing]

  return <Card className="estimator phase2-estimator">
    <div className="estimator-inputs">
      <span className="icon-tile"><Calculator size={21} /></span>
      <h3>Save &amp; Send credit estimator</h3>
      <p>Estimate monthly Sales Invoices, Credit Memos, and your access needs.</p>
      <div className="estimator-fields">
        {fields.map(({ key, label, min }) => {
          const id = 'estimate-' + key
          const error = !parsed.success && parsed.error.issues.some(issue => issue.path[0] === key)
            ? 'Enter a whole number from ' + min + ' to ' + formatCount(maxUsage) + '.' : undefined
          const hint = key === 'salesOrders' ? id + '-hint' : undefined
          return <Field key={key} id={id} label={label} error={error}>
            <Input id={id} type="number" min={min} max={maxUsage} step={1} inputMode="numeric" aria-invalid={!!error}
              aria-describedby={[hint, error && id + '-error'].filter(Boolean).join(' ') || undefined} {...register(key, { valueAsNumber: true })} />
            {hint && <small className="estimator-hint" id={hint}>Operational estimate only. Uses 0 credits.</small>}
          </Field>
        })}
      </div>
    </div>
    <div className="estimator-result">
      <div aria-live="polite" aria-atomic="true">
        <span>{period.label} billing recommendation</span>
        <strong className="estimate-price">{plan?.name ?? (catalog.isPending ? 'Loading plans…' : catalog.isError ? 'Plans unavailable' : result ? 'No suitable plan' : '—')}</strong>
        <dl>
          <div><dt>Monthly Save &amp; Send Credits</dt><dd>{result ? formatCount(result.monthly) : '—'}</dd></div>
          <div><dt>Annual Save &amp; Send Credits</dt><dd>{result ? formatCount(result.annual) : '—'}</dd></div>
          <div><dt>Additional Credits / {period.unit}</dt><dd>{result?.additional == null ? '—' : formatCount(result.additional)}</dd></div>
        </dl>
        <p>{result && !plan && catalog.isSuccess
          ? 'These company file or user requirements exceed the illustrative allowances.'
          : result?.additional
            ? 'This plan has the largest eligible allowance. Use Additional Credits to cover your estimate.'
            : 'Recommendations use credit, company file, and user allowances. Annual estimates assume consistent monthly usage.'}</p>
      </div>
      {plan && <Button asChild><Link to={registrationUrl(plan, billing)}>Select {plan.name} <ArrowRight size={16} /></Link></Button>}
    </div>
  </Card>
}

