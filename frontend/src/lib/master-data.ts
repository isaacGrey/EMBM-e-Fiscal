export type MasterValue = string | boolean | null | MasterValue[] | { [key: string]: MasterValue }
export type MasterFields = Record<string, MasterValue>
export type MasterField = {
  key: string; label: string; type: string; required: boolean; choices: string[] | null; reference: string | null; maxLength: number
}
export type MasterDefinition = {
  key: string; label: string; fields: MasterField[]; effective: boolean; canWrite: boolean; canImport: boolean; canExport: boolean
}
export type MasterRow = { id: string; code: string; name: string; active: boolean; revision: number; updatedAt: string; data: MasterFields; verificationStatus: string | null }
export type LookupRow = Pick<MasterRow, 'id' | 'code' | 'name' | 'active'>
export type AuditRow = { revision: number; name: string; active: boolean; effectiveFrom?: string; effectiveThrough?: string; actorId: string; at: string; action: string; data: MasterFields }
export type MasterDetail = { record: MasterRow; history: AuditRow[] }
export type MasterPage = { items: MasterRow[]; total: number; page: number; pageSize: number }
export type MasterMenu = {
  definitions: MasterDefinition[]; customFields: MasterRow[]; rdos: { code: string; name: string }[]
  regulatoryReferences: { id: string; label: string; authority: string; source: string; supportedFrom: string; verificationStatus: string; restrictions: string }[]
  recordTypes: string[]; canCalculate: boolean
}
export type ImportPreview = { digest: string; previewToken: string | null; total: number; valid: number; errors: { row: number; field: string; message: string }[] }
export type ImportResult = { imported: number; replay: boolean }

export const masterRoot = '/portal/client/master-data'
export const masterQueryKey = (base: string) => ['master-data', base]
export const asText = (value?: MasterValue) => typeof value === 'string' ? value : ''
export const asList = (value?: MasterValue) => Array.isArray(value) ? value : []
export const asObject = (value?: MasterValue) => value && typeof value === 'object' && !Array.isArray(value) ? value : {}
export const optionLabel = (value: string) => ({
  VATable: 'VATable', VATExempt: 'VAT exempt', ZeroRated: 'Zero-rated', NonVAT: 'Non-VAT',
  SeniorCitizen: 'Senior citizen', PWD: 'Person with disability', SoloParent: 'Solo parent', AthleteCoach: 'Athlete or coach',
  HalfAway: 'Half away from zero', HalfEven: 'Half to even', NetSales: 'Net sales', GrossTotal: 'Gross total',
  CreditableWithholding: 'Creditable withholding tax', WithholdingVAT: 'Withholding VAT', ReduceBase: 'Reduce tax base',
  CompanyAuthorizationRequired: 'Company authorization required', AuthorizedForPreview: 'Authorized for preview',
  ConfigurationRequiresReview: 'Configuration requires review', ReferenceOnly: 'Reference only', VerifiedScope: 'Verified scope',
} as Record<string, string>)[value] ?? value.replace(/([a-z])([A-Z])/g, '$1 $2').replaceAll('-', ' ')

