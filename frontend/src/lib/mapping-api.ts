export const directions = ['Outbound', 'Inbound', 'Response'] as const
export const documentKinds = ['SalesInvoice', 'CreditMemo', 'SalesOrder'] as const
export const directionLabels = { Outbound: 'eFiscal to accounting', Inbound: 'External source to eFiscal', Response: 'External response' }
export const documentKindLabels = { SalesInvoice: 'Sales Invoice', CreditMemo: 'Credit Memo', SalesOrder: 'Sales Order' }
export type Direction = typeof directions[number]
export type DocumentKind = typeof documentKinds[number]
export type MappingField = { code: string; label: string; scope: 'Header' | 'Line'; type: string; required: boolean; financial: boolean; maxLength: number;
  scale: number; reference?: string; allowed?: string[]; readOnly?: boolean; financialReference?: boolean }
export type FieldRule = { scope: string; target: string; operation: string; sources: string[]; value?: unknown; fallback?: unknown; values?: Record<string, unknown> }
export type MappingProfile = { id: string; connectionId: string; connectionName: string; name: string; documentType: DocumentKind;
  direction: Direction; operation: string; activeVersion?: number; disabled: boolean; connectorVersionId: string }
export type MappingVersion = { number: number; status: string; rules: FieldRule[]; validation: string[]; connectorVersionId: string }
export type ProfileDetail = { profile: MappingProfile; versions: MappingVersion[]; templates: { id: string; name: string; version: number }[];
  fields: { source: MappingField[]; destination: MappingField[] }; operations: string[]; defaults: FieldRule[] }
export type PreviewResult = { valid: boolean; errors: string[]; source?: unknown; payload?: unknown; request?: unknown; expectedResponse?: unknown }
export type BoundReference = { kind: string; localKey: string; externalId: string; externalCode: string }
export type ReferenceChoices = { kinds: string[]; kind: string; choices: BoundReference[]; items: BoundReference[];
  local: { id: string; code: string; name: string }[]; suggestions: { id: string; externalId: string }[] }
export type AccountingJob = { id: string; number: string; type: DocumentKind; date: string; status: string; branchId: string;
  connectionId?: string; externalNumber?: string }
export type JobDetails = { id: string; number: string; type: DocumentKind; status: string; identity?: {
  internalNumber: string; externalId: string; externalType: string; externalNumber: string; externalCompany: string; status: string; mappingNumber: number; confirmedAt: string;
}; attempts: { id: string; number: number; status: string; category?: string; evidence?: string; startedAt: string; mappingNumber: number }[];
  profileId?: string; mappingVersion?: number; canManage: boolean; canVerify: boolean; canRetry: boolean }
export type Destination = { id: string; documentType: DocumentKind; branchId: string | null; profileId: string; profile: string; externalResourceName: string;
  connection: string; health: string; enabled: boolean; automatic: boolean; scenario: string }
export const pretty = (x: string) => x.replace(/([a-z])([A-Z])/g, '$1 $2')

