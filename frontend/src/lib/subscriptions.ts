import { api } from './account-api'

export const billingPeriods = {
  monthly: { label: 'Monthly', unit: 'month', months: 1 },
  annual: { label: 'Annual', unit: 'year', months: 12 },
} as const
export type Billing = keyof typeof billingPeriods

export const documentTypes = [
  { key: 'invoices', name: 'Sales Invoice', label: 'Monthly Sales Invoices', action: 'CommitAndSend', credits: 1 },
  { key: 'creditMemos', name: 'Credit Memo', label: 'Monthly Credit Memos', action: 'CommitAndSend', credits: 1 },
  { key: 'salesOrders', name: 'Sales Order', label: 'Monthly Sales Orders', action: 'CommitOnly', credits: 0 },
] as const

const saveAndSendTypes = documentTypes.filter(type => type.action === 'CommitAndSend')

export type DocumentVolumes = Record<(typeof documentTypes)[number]['key'], number>
export type SubscriptionUsage = DocumentVolumes & { companyFiles: number; users: number }

export type Plan = {
  versionId: string
  version: number
  accountingConnections: number
  connectorCategories: string[]
  allocationFrequency: 'monthly' | 'period'
  creditValidityDays: number | null
  rollover: boolean
  graceDays: number
  id: string
  name: string
  description: string
  billing: Record<Billing, { price: number; credits: number }>
  companyFiles: number
  users: number
  integrations: readonly string[]
  capabilities: readonly string[]
  renewal: boolean
  autoTopUps: boolean
}

export type CreditPackage = {
  id: string; versionId: string; version: number; name: string; credits: number; price: number; currency: string
  validityDays: number | null; eligiblePlans: string[]
}
export type Catalog = { plans: Plan[]; packages: CreditPackage[]; checkoutAvailable: boolean }
export const catalogQuery = { queryKey: ['billing-catalog'], queryFn: () => api<Catalog>('/billing/catalog'), staleTime: 30_000, retry: false } as const

export function isBilling(value: string | null): value is Billing {
  return value === 'monthly' || value === 'annual'
}

export function pricingUrl(billing: Billing) {
  return '/pricing?billing=' + billing
}

export function registrationUrl(plan: Plan, billing: Billing) {
  return '/get-started?' + new URLSearchParams({ plan: plan.id, billing }).toString()
}

export function estimateSubscription(usage: SubscriptionUsage, billing: Billing, catalog: readonly Plan[]) {
  const monthly = saveAndSendTypes.reduce((total, document) => total + usage[document.key] * document.credits, 0)
  const annual = monthly * billingPeriods.annual.months
  const required = monthly * billingPeriods[billing].months
  const eligible = catalog.filter(plan => plan.companyFiles >= usage.companyFiles && plan.users >= usage.users)
  const byPrice = (a: Plan, b: Plan) => a.billing[billing].price - b.billing[billing].price
  const covered = eligible.filter(plan => plan.billing[billing].credits >= required).sort(byPrice)
  const fallback = eligible.sort((a, b) => b.billing[billing].credits - a.billing[billing].credits || byPrice(a, b))
  const recommended = covered[0] ?? fallback[0] ?? null
  const additional = recommended ? Math.max(0, required - recommended.billing[billing].credits) : null

  return { monthly, annual, recommended, additional }
}

export const formatCount = new Intl.NumberFormat('en-PH').format
