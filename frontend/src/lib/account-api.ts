export class ApiError extends Error {
  constructor(readonly status: number, message: string, readonly fields: Record<string, string[]> = {}) { super(message) }
}

async function readResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}))
    throw new ApiError(response.status, problem.title ?? 'The request could not be completed.', problem.errors)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

async function accountRequest(path: string, method: string, payload?: unknown) {
  const headers: Record<string, string> = { Accept: 'application/json' }
  const multipart = payload instanceof FormData
  if (method !== 'GET') {
    const token = await fetch('/api/auth/csrf', { headers, cache: 'no-store' }).then(readResponse<{ token: string }>)
    headers['X-EFiscal-CSRF'] = token.token
    if (!multipart && payload !== undefined) headers['Content-Type'] = 'application/json'
  }
  return fetch('/api' + path, { method, headers, cache: 'no-store',
    body: multipart ? payload : payload === undefined ? undefined : JSON.stringify(payload) })
}

export async function api<T = void>(path: string, method = 'GET', payload?: unknown): Promise<T> {
  return readResponse<T>(await accountRequest(path, method, payload))
}

export async function apiBlob(path: string, method: string, payload?: unknown): Promise<Blob> {
  const response = await accountRequest(path, method, payload)
  if (!response.ok) await readResponse(response)
  return response.blob()
}

export const errorMessage = (error: unknown) => error instanceof ApiError
  ? Object.values(error.fields).flat()[0] ?? error.message
  : 'The service is unavailable. Try again.'

export type Session = {
  id: string; fullName: string; mobile: string; email: string
  kind: 'Customer' | 'Vendor' | 'Regulator'; status: string; pending: PendingSubscription | null
}
export type PendingSubscription = { planId: string; billing: 'monthly' | 'annual'; classification: 'b2b' | 'b2c'; status: 'PendingPayment' }

export async function currentUser() {
  try { return await api<Session>('/auth/me') }
  catch (error) {
    if (error instanceof ApiError && error.status === 401) return null
    throw error
  }
}

export const sessionQuery = { queryKey: ['account-session'], queryFn: currentUser, retry: false } as const

export type Subscriber = { id: string; name: string; planId: string; status: string; roleId: string; companyLimit: number; userLimit: number; developmentFixture: boolean }
export type ActiveContext = { subscriberId: string; companyId: string; branchId: string | null }
export type WorkspaceData = { subscribers: Subscriber[]; active: ActiveContext | null; pending: PendingSubscription | null }
export type CompanyInput = { code: string; registeredName: string; tradeName: string; businessClassification: string; address: string; email: string; phone: string }
export type TaxpayerInput = { tin: string; vatStatus: string; rdoCode: string; effectiveFrom: string; registrationDetails: string; permitReference: string }
export type BranchInput = { type: string; code: string; name: string; address: string; taxRegistration: string; rdoCode: string; contact: string }
export type Branch = Omit<BranchInput, 'rdoCode'> & { id: string }
export type CompanyList = { companies: { id: string; code: string; registeredName: string }[]; permissions: string[]; canAddCompany: boolean }
export type RdoReference = { code: string; name: string; developmentFixture: boolean }
export type RdoAssignment = { id: string; branchId: string | null; code: string; name: string; effectiveFrom: string; effectiveUntil: string | null; registrationStatus: string }
export type CompanyDetails = { company: CompanyInput; taxpayer: Omit<TaxpayerInput, 'rdoCode' | 'effectiveFrom'> & { verificationStatus: string; issuanceStatus: string };
  configurationStatus: string; assignments: RdoAssignment[]; evidence: { id: string; name: string; uploadedAt: string }[] }
export type TeamData = { members: { userId: string; fullName: string; email: string; roleId: string; companyIds: string[] }[];
  invitations: { id: string; email: string; roleId: string; acceptedAt: string | null; expiresAt: string }[];
  roles: { id: string; name: string; permissions: string[] }[]; canManage: boolean }

export function manilaDate() {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Manila', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts()
  return ['year', 'month', 'day'].map(type => parts.find(part => part.type === type)?.value).join('-')
}
export const manilaTimestamp = new Intl.DateTimeFormat('en-PH', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Manila' })

