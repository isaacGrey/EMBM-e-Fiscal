import { ArrowDownUp, Building2, ClipboardCheck, FileCheck2, FileText, Fingerprint, Plug2, ReceiptText, ShieldCheck } from 'lucide-react'

export const features = [
  { icon: FileText, title: 'Electronic Invoicing', text: 'Create and issue electronic invoices through a dedicated Philippine e-Invoicing platform.', points: ['Sales invoice issuance', 'Electronic invoice delivery'] },
  { icon: ReceiptText, title: 'Philippine Tax Configuration', text: 'Support applicable Philippine tax classifications and verified statutory discount rules.', points: ['Philippine VAT configuration', 'Applicable statutory discount groups'] },
  { icon: ArrowDownUp, title: 'Accounting Integration', text: 'Connect electronic invoice workflows with existing accounting software.', points: ['Configurable accounting field mapping', 'Invoice synchronization and retry status'] },
  { icon: ClipboardCheck, title: 'Electronic Invoice Reconciliation', text: 'Compare committed electronic invoice records with actual connected accounting records.', points: ['Invoice matching across systems', 'Discrepancies and traceable corrections'] },
  { icon: ShieldCheck, title: 'BIR Reporting Readiness', text: 'Prepare electronic invoice information for officially supported and authorized reporting methods.', points: ['Company TIN, registered RDO, and branch details', 'Reporting subject to formal authorization'] },
  { icon: Fingerprint, title: 'Secure Invoice Records', text: 'Preserve immutable committed invoice records and authorized audit histories.', points: ['Permanent records after Save & Send', 'Authorized, linked corrections'] },
]

type Integration = { id: string; name: string; mark: string; group: string; kind: 'accounting' | 'commerce'; tint: string; status: 'Planned' | 'In Development' | 'Available' | 'Setup Required' }

export const integrations: readonly Integration[] = [
  { id: 'autocount', name: 'AutoCount Accounting', kind: 'accounting', status: 'Planned', mark: 'Ac', group: 'External accounting system', tint: 'blue' },
  { id: 'qbd', name: 'QuickBooks Desktop', kind: 'accounting', status: 'Planned', mark: 'qb', group: 'External accounting system', tint: 'green' },
  { id: 'qbo', name: 'QuickBooks Online', kind: 'accounting', status: 'Planned', mark: 'qb', group: 'External accounting system', tint: 'green' },
  { id: 'accountingsuite', name: 'AccountingSuite / 1C', kind: 'accounting', status: 'Planned', mark: '1C', group: 'External accounting system', tint: 'gold' },
  { id: 'shopee', name: 'Shopee', kind: 'commerce', status: 'Planned', mark: 'S', group: 'Source orders for e-Invoicing', tint: 'orange' },
  { id: 'tiktok', name: 'TikTok Shop', kind: 'commerce', status: 'Planned', mark: 'Tk', group: 'Source orders for e-Invoicing', tint: 'ink' },
]

export const steps = [
  { title: 'Set up invoice issuance', text: 'Configure your company TIN, registered RDO, branch details, tax groups, and team permissions.', icon: Building2 },
  { title: 'Connect your accounting software', text: 'Set up an accounting connector and map invoice fields for each company you manage.', icon: Plug2 },
  { title: 'Issue and reconcile invoices', text: 'Use Save & Send to commit and issue an invoice. Track electronic delivery and check the invoice against its accounting record.', icon: FileCheck2 },
]

export const faqs = [
  ['What are Save & Send Credits?', 'A successfully committed Save & Send for a Sales Invoice or applicable Credit Memo uses one credit by default. Failed commitments, Sales Order saving, and accounting synchronization use zero credits.'],
  ['Can I edit an invoice after Save & Send?', 'Committed electronic invoices are permanent. Corrections use separate authorized linked transactions or lifecycle events so the original stays traceable.'],
  ['Does a Sales Order count as an electronic invoice?', 'Sales Orders are supporting commercial documents. Save commits the order with zero credit use. The order syncs to connected accounting software and does not enter electronic invoice delivery or BIR reporting. A later invoice referencing that order uses credits only when Save & Send successfully commits the invoice.'],
  ['Do resends and retries use credits?', 'Accounting synchronization, resending a committed invoice, and delivery or BIR reporting retries use zero credits. A delivery failure after commitment leaves the original charge in place.'],
  ['Are the listed integrations available today?', 'The listed accounting and e-commerce connectors are planned. EMBM eFiscal complements your existing accounting software, which remains your accounting system.'],
  ['Are subscriptions and credit purchases available?', 'You can compare illustrative plans, register, and verify your email. The development portal supports simulated subscription purchases, renewals, and credit top-ups. Live payments are pending provider approval.'],
  ['Does eFiscal transmit electronic invoices to the BIR?', 'Live BIR transmission is disabled. Future electronic invoice reporting must use officially supported methods and requires formal authorization. Read-only BIR access is restricted to authorized grants.'],
]

