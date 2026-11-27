export type SenderProfile = {
  senderName: string; senderEmail: string; replyTo: string | null; provider: string
  smtpHost: string | null; smtpPort: number | null; tlsMode: string | null; authentication: string | null
  cc: string[]; bcc: string[]
}
export type SenderData = {
  profile: SenderProfile | null; revision: number; updatedAt: string | null; senderVerified: boolean; state: string
  connectionStatus: string; lastSuccessfulTestAt: string | null
  testMailboxAvailable: boolean; sampleHistory: { at: string; status: string }[]
}
export type DocumentType = 'invoice' | 'credit-memo'
export type TemplateKind = 'email' | 'pdf'
export type EmailTemplate = {
  subject: string; body: string; brand: string | null; signature: string | null; footer: string | null; includeConfiguredCopies: boolean
}
export type PdfLayout = {
  title: string; header: string | null; businessLayout: string; customerLayout: string; columns: string[]
  font: string; accentColor: string; marginMm: number; logo: string | null; paymentInstructions: string | null
  terms: string | null; footer: string | null; signatureField: boolean; pageNumbers: boolean
  pageSize?: 'A4' | 'Letter'; fontSize?: number; columnWeights?: number[] | null; summaryPosition?: 'left' | 'right'
}
export type TemplateContent = EmailTemplate | PdfLayout
export type TemplateInfo = { id: string; name: string; kind: TemplateKind; documentType: DocumentType; isDefault: boolean; currentVersion: number; updatedAt: string }
export type TemplateList = { templates: TemplateInfo[]; defaultContent: TemplateContent; variables: string[] }
export type TemplateDetails = { template: TemplateInfo; content: TemplateContent; history: { version: number; name: string; createdAt: string }[] }
export type EmailSample = { subject: string; body: string; brand: string; signature: string; footer: string; recipient: string }

