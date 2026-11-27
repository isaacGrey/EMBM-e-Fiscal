export const reconciliationRoot = '/portal/client/reconciliation'
export const coverageRoot = '/portal/client/integrations/reconciliation-settings'
export const typeNames = { SalesInvoice: 'Sales Invoice', CreditMemo: 'Credit Memo', SalesOrder: 'Sales Order' }
export type ReconcileType = keyof typeof typeNames
export const status = (text?: string | null) => text ? ({
  EfiscalOnly: 'eFiscal-only', MissingEfiscalRecord: 'Missing eFiscal Record', HistoricalExcluded: 'Historical — Excluded',
  OutsideCoveragePeriod: 'Outside Coverage Period', NotYetReconciled: 'Not Yet Reconciled',
}[text] ?? text.replace(/([a-z])([A-Z])/g, '$1 $2')) : 'Unavailable'

export type AccountingLink = { id: string; name: string; status: string; externalResourceName: string; externalResourceId: string;
  externalAccountId: string; branchId?: string }
export type Coverage = { id: string; connectionId: string; number: number; adoption: string; start: string; end?: string; mode: string;
  types: ReconcileType[]; excludedSources: string[]; exclusionReason?: string; issuanceScope: string; timeZone: string; reason: string;
  createdAt: string; createdBy: string; approvedBy?: string; effectiveAt?: string; active: boolean; amountTolerance: number;
  freshnessHours: number; intervalHours?: number; afterSynchronization: boolean; batchSize: number }
export type CoverageList = { links: AccountingLink[]; items: Coverage[]; canManage: boolean }
export type CoveragePreview = { valid: boolean; proposed: Coverage; pinnedCommitted: number; priorChecksWithFindings: number;
  knownHistory?: number; knownOutside?: number; historyTotalKnown: boolean }
export type ReconcileCheck = { id: string; documentId?: string; connectionId: string; coverageId?: string; type: ReconcileType;
  number?: string; externalNumber?: string; businessDate?: string; currency?: string; expectedTotal?: number; actualTotal?: number; difference?: number;
  coverage: string; retrieval: string; completeness: string; status: string; categories: string[]; findingCount: number; externalChanged: boolean;
  synchronization: string; at: string; stale: boolean }
export type Finding = { id: string; checkId: string; category: string; field: string; severity: string; detectedAt: string; status: string;
  reviewer?: string; history: { id: string; actor?: string; reviewer?: string; verificationId?: string; status: string; at: string; note?: string }[] }
export type Comparison = { check: ReconcileCheck; cells: { field: string; line?: string; category: string; state: string;
  expected?: unknown; actual?: unknown; difference?: number }[]; history: ReconcileCheck[]; findings: Finding[]; coverage?: Coverage;
  snapshot?: { id: string; connectorVersionId: string; normalizationVersion?: number; account: string; externalCompany: string; externalId: string;
    number: string; retrievedAt: string; modifiedAt?: string; hash: string; evidence: string };
  canRun: boolean; canReview: boolean; canAssign: boolean; canSimulate: boolean; reviewers: { id: string; fullName: string }[] }
export type ReconciliationOverview = { eligible: number; conclusive: number; exact: number; withinTolerance: number; partial: number;
  findings: number; discrepancyUnits: number; missingAccounting: number; missingEfiscal: number; duplicates: number;
  awaiting: number; notYetVerified: number; stale: number; latest?: string; observedHistorical?: number; observedOutside: number;
  matchRate?: number; verificationCoverage?: number }
export type ReconcileJob = { id: string; connectionId: string; documentId?: string; kind: string; state: string; failure?: string; createdAt: string;
  completedAt?: string; attempts: number }
export type ReferenceRow = { id: string; number: string; date?: string; type: ReconcileType; source: string; status?: string }
export type References = { items: ReferenceRow[]; creditMemoEligibility: string; saved: { id: string; number: string; at: string; purpose: string }[] }
export type DocumentReconciliation = { items: ReconcileCheck[]; connection?: string; canRun: boolean }

