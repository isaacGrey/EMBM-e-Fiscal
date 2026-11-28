export type Capability = { code: string; verification: 'Proposed' | 'Development Verified'; documentation?: string | null }
export type ConnectorConfiguration = {
  name: string; description: string; deployment: string; apiVersion: string; editions: string[]; regions: string[];
  documentation: string[]; requirements: string[]; authentication: string[]; priority: string; logoLabel: string;
  capabilities: Capability[]; origins: string[]; manualOperations: { method: string; path: string }[];
}
export type ConnectorCard = ConnectorConfiguration & {
  versionId: string; connectorId: string; version: number; code: string; category: string; availability: string; development: boolean; canConnect: boolean;
}
export type Operation = { method: string; path: string; parameters: { name: string; location: string; required: boolean; schema: unknown }[];
  request?: unknown; response?: unknown }
export type SpecReview = { title: string; format: string; version: string; servers: string[]; authentication: string[]; endpoints: Operation[]; warnings: string[] }
export type ConnectorVersion = {
  id: string; connectorId: string; code: string; number: number; revision: number; category: string; adapter: string; state: string;
  availability: string; effectiveAvailability: string; disabled: boolean; lifecycle: 'Deprecated' | 'Retired' | null; configuration: ConnectorConfiguration; specification: SpecReview | null;
  revocation: { at: string; reason: string } | null;
  validation: { valid: boolean; errors: string[]; warnings: string[]; validatedAt: string } | null;
}
export type Connection = {
  id: string; name: string; branchId: string | null; externalAccountId: string; externalResourceId: string; externalResourceName: string;
  resourceType: 'CompanyFile' | 'Store'; connector: string; connectorCode: string; connectorVersionId: string; version: number; development: boolean;
  status: string; authorization: string; mappingStatus: string; mappingVersion: number | null; lastSuccessfulTest: string | null;
  lastError: string | null; createdAt: string;
}
export type Authorization = {
  proof: string; versionId: string;
  account: { id: string; name: string; resources: { id: string; name: string; type: 'CompanyFile' | 'Store' }[] };
}
export type MappingInfo = {
  template: string; fields: { location: string; name: string; type: string; required: boolean; allowedValues?: string[] }[];
  versions: { number: number; customerId: string; itemId: string; unitId: string; shippingItemId?: string | null; sellerDiscountGroupId?: string | null; createdAt: string }[];
}
export type ProviderOrder = {
  id: string; reference: string; orderedOn: string; currency: string; status: string; customerCode: string; customerName: string;
  lines: { id: string; itemCode: string; description: string; quantity: number; invoiceableQuantity: number; price: number; discount: number; taxCode: string | null }[];
  shipping: number; refund: number; cancelled: boolean;
}
export type SourceOrder = {
  id: string; connectionId: string; provider: string; shop: string; shopId: string; externalOrderId: string; reference: string;
  date: string; status: string; currency: string; retrievedAt: string; order: ProviderOrder; key: string | null;
}
export type ConnectionLog = { at: string; action: string; actorId?: string; connection: string; id: string };
export const connectRoot = '/portal/client/integrations'
export const invoiceFromSource = (key: string) => '/portal/client/documents/SalesInvoice/new?externalSource=' + encodeURIComponent(key)

