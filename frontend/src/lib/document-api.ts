export const documentTypes = ['SalesInvoice', 'CreditMemo', 'SalesOrder'] as const
export type DocumentType = typeof documentTypes[number]
export const documentLabels: Record<DocumentType, string> = { SalesInvoice: 'Sales Invoice', CreditMemo: 'Credit Memo', SalesOrder: 'Sales Order' }
export const documentsRoot = '/portal/client/documents'
export type Choice = { id: string; code: string; name: string }
export type TemplateChoice = { id: string | null; name: string; version: number }
export type DocumentOptions = {
  canWrite: boolean; canCommit: boolean; canExport: boolean; canResend: boolean; canBuyCredits: boolean; development: boolean
  availableCredits: number | null; branches: Choice[]
  templates: { id: string; name: string; kind: 'pdf' | 'email'; isDefault: boolean; currentVersion: number }[]
}
export type DocumentReceipt = {
  id: string; type: DocumentType; number: string; date: string; customerId: string; customerCode: string; customerName: string
  branchId: string; currency: string; total: string; amountDue: string; credits: number; committedAt: string
  developmentFixture: boolean; sourceDocumentId?: string; externalOrderNumber?: string
}
export type DocumentRegisterRow = { document: DocumentReceipt; sourceNumber?: string; rendering: string; electronicDelivery: string; accounting: string; regulator: string }
export type DocumentRegisterData = { items: DocumentRegisterRow[]; total: number; page: number; pageSize: number }
export type DocumentParty = { name: string; code: string; tin?: string; rdo?: string; registeredAddress?: string; billingAddress?: string; email?: string }
export type DocumentLine = {
  id: string; position: number; itemId: string; itemCode: string; description: string; unitId: string; unitCode: string; quantity: string
  unitPrice: string; gross: string; commercialDiscount: string; statutoryDiscount: string; vat: string; total: string; terms: unknown
}
export type DocumentPreview = {
  confirmation: string; type: DocumentType; date: string; dueDate: string; currency: string; exchangeRate: string; precision: number
  customerId: string; customerName: string; customerCode: string; documentNumber: null; seriesConfigured: boolean; senderConfigured: boolean
  totals: Record<string, string>; parties: { customer: DocumentParty; seller: DocumentParty }; lines: DocumentLine[]
  recipients: string[]; recipient: string | null; pdfTemplate?: TemplateChoice; emailTemplate?: TemplateChoice
  availableCredits: number | null; credits: number; remainingCredits: number | null
}
export type DeliveryAttempt = {
  id: string; jobId: string; attempt: number; status: string; recipient: string; providerReference?: string; attachmentSha256?: string
  errorCategory?: string; error?: string; at: string; retryAt?: string
}
export type DocumentDetail = {
  document: DocumentReceipt; totals: Record<string, string>; dueDate: string; precision: number; exchangeRate: string
  parties: { seller: DocumentParty; customer: DocumentParty; branchCode: string; branchName: string }; header: Record<string, unknown>
  numbering: Record<string, unknown>; templates: { pdf?: TemplateChoice; email?: TemplateChoice }; lines: DocumentLine[]
  rendering: string; electronicDelivery: string; accounting: string; regulator: string; file?: { sha256: string; renderedAt: string }
  related: DocumentReceipt[]; delivery: DeliveryAttempt[]; audit: { actorId?: string; action: string; at: string }[]
}
export type SourceLine = {
  id: string; itemId: string; itemCode: string; description: string; unitId: string; unitCode: string; remainingQuantity: string
  usedQuantity: string; usedAmount: string; remainingAmount: string; unitPrice: string; quantity: string
}
export type SourceDocument = { document: DocumentReceipt; partialFulfillment: boolean; lines: SourceLine[] }
export type ExternalSource = {
  financialReview?: string | null;
  key: string; number: string; connector: string; account: string; customerId: string; currency?: string; branchId?: string
  lines: { key: string; itemId: string; unitId: string; quantity: string; remainingQuantity: string }[]
}
export type EmailPreview = { subject: string; body: string; brand: string; signature: string; footer: string; recipient: string | null; preview: boolean }
export const procedureName = (type: DocumentType) => type === 'SalesOrder' ? 'Save' : 'Save & Send'
export const statusName = (value: string) => value === 'AcceptedByProvider' ? 'Accepted by development adapter' : value.replace(/([a-z])([A-Z])/g, '$1 $2')

