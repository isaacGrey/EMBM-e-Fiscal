# PostgreSQL schema

`EfiscalDbContext` is the single store for the API. The additive `20261126065158_IdentityAndTenantFoundation` migration creates 20 application tables and seeds six roles and their permission grants. Use PostgreSQL 15 or newer for the current-assignment uniqueness constraint.

| Area | Tables | Integrity |
| --- | --- | --- |
| Identity | Users, UserIdentities, Registrations, IdentityTokens, Sessions, SecurityAudits | One credential and optional purchasing intent per user. Normalized email is globally unique. Verification and reset tokens store a digest, purpose, expiry and consumption timestamp. Session rows bind an expiry and security stamp to the user. |
| Subscriber | Subscribers, SubscriberMemberships, Roles, PermissionDefinitions, RolePermissions | A membership joins exactly one user, subscriber and role. Grants are separate rows. Commercial status and plan allowances are separate from identity status. |
| Company | Companies, CompanyMemberships, TaxpayerProfiles, Branches | Company codes are unique within a subscriber. Company access joins through subscriber membership. Composite foreign keys prevent cross-subscriber company and branch references. Branch codes are unique per company, with at most one head office. |
| RDO | RdoReferences, RdoAssignments | Codes are text. Company and branch assignments have effective dates and a saved office name. A filtered unique index permits one open assignment per scope, including the null branch used for a company. A check constraint requires an end date after the start date. |
| Invitation | Invitations, InvitationCompanies | Inviter membership and selected company assignments are tenant-scoped. Invitation digests are unique, with independent expiration and acceptance fields. |
| Evidence | RegistrationEvidence | Private PDF bytes and metadata belong to a tenant-scoped company. API lists metadata only. |

Timestamps use UTC. Effective dates are calendar dates in Asia/Manila. Identity, commercial status, configuration, regulatory verification and issuance authorization remain independent. Configuration creates a company with the latter two states set to NotVerified and Disabled. The identity migration introduces the registration, customer and tenant foundation.

## Company delivery configuration

The additive CompanyDeliveryConfiguration migration adds:

| Table | Scope and constraints |
| --- | --- |
| CompanyEmailSettings | One row per subscriber and company via a composite company foreign key. Stores validated non-secret preferences as JSONB, a revision, and modification time. No SMTP or OAuth secrets. |
| CompanyTemplates | Subscriber and company composite foreign key. A filtered unique index permits one custom default per company, template kind and document type. |
| CompanyTemplateVersions | Composite primary key of subscriber, company, template and version. The composite foreign key prevents references to another company's template. Stores validated content and name; saves and restores append a row. |

Settings and local test outcomes are audited using SecurityAudits. Issued document snapshots, wallet entries, provider secrets, outbound attempt history, and external reference or fulfillment records are reserved for the corresponding financial and integration boundaries.

## Subscription, payment and Save & Send credits

Migration 20261126073833_SubscriptionPaymentsAndCreditWallet adds the commercial tables to the shared context.

| Area | Tables and constraints |
| --- | --- |
| Catalog | PlanVersions and PackageVersions use a unique code and version, an effective date and an active flag. Price fields use numeric(18,2). Previously effective and scheduled terms remain available for audits. |
| Payments | CheckoutOrders link the purchaser, subscriber, commercial purpose, version and exact PHP amount. A purchaser's operation key is unique. Payments store provider references, status, timestamps and refund information. PaymentEvents deduplicates provider event keys and stores payload digests. |
| Subscription | SubscriberSubscriptions pins the current version and period payment. BillingPaymentMethods stores an opaque mock reference. AutoTopUpPreferences stores explicit consent, per-transaction and period limits, and the selected package version. |
| Credits | CreditWallets permits one wallet per subscriber. CreditLots stores whole-unit balances, reservations, expiry and paid allocation slices. CreditReservations assigns a unique operation key within the wallet. CreditLedgerEntries records the financial history with one consumption per tenant, document type and document ID. |
| Immutability | PostgreSQL triggers reject row updates and deletions in the payment event and credit ledger tables and the versioned commercial catalog. Refunds and authorized corrections append ledger entries. |



## Company master data and effective-dated rules

Migration `20261126085905_CompanyMasterDataAndFinancialRules` adds the company master-data tables and seeds the corresponding grants.

| Table | Scope and constraints |
| --- | --- |
| MasterRecords | Master identity keyed by subscriber, company and record ID, with a unique normalized code per company and kind. A validated kind schema covers customers, customer groups, items, categories, units and conversions, rules, pricing, terms, currencies and rates, dimensions, and custom definitions. |
| MasterRevisions | Composite record foreign key. Append-only snapshots contain effective dates, active status, values, actor, action and timestamp. The database rejects updates and deletes. |
| MasterLinks | Source and target foreign keys include subscriber and company. Links support scoped default groups, prices, terms, units and typed custom references. |
| ExternalMasterReferences | Stable IDs unique by subscriber, company, connector, external account, record type and external ID. Connector metadata includes the visible code and source timestamp. |
| MasterImportOperations | Scope, actor, file digest and row count for each successful import. The scoped digest makes a confirmed import retry idempotent. |

Money, rates and conversions use validated C# decimals and exact PostgreSQL JSONB numeric scalars with up to eight fractional places. API decimal inputs and outputs are invariant strings. Customer addresses, recipients, custom values and rule components are structured within the corresponding records and immutable revisions. PHP is built in. Exchange-rate records require an authorized source.


## Committed documents and processing

Migration `20261126094118_CommittedDocumentsAndProcessing` adds scoped documents, lines and snapshots, branch number series, commitment results, original-source allocations, processing states, outbox work, preserved PDFs and delivery attempts. Composite keys keep owner relations within the company. PostgreSQL guards committed content and dated configuration versions against mutation and checks commitment consistency at transaction end. PDF records preserve the original bytes and SHA-256 digest. Processing state, retry scheduling and delivery attempts have a separate lifecycle from issued fiscal values.

## Connector schema requirements

Future migrations separate stable vendor definitions, immutable versioned specs and capability grants, approval and publication events, company connections and account bindings, mapping revisions, secret reference metadata, jobs and sanitized execution history. Workspace records use subscriber and company keys. Provider identities include connector, bound account and source identifier. Publication derives from approved versions. Active mappings stay pinned across connector upgrades. Connection authorization is scoped to the bound company account. Store credential material in an approved secret store.


### Phase 7 additive migration

Migration `20261127003905_ConnectorRegistryAndCompanyConnections` adds `Connectors`, `ConnectorVersions`, `VendorConnectorGrants`, `ExternalConnections`, `ConnectionCredentials`, `ConnectionMappings`, `SourceOrders` and `DesktopAgentRegistrations`. The migration seeds priority listings and two published synthetic connector versions. The normalized spec review, capabilities and validation results use bounded JSONB documents. The vendor grants distinguish viewing, editing, and publication.

Composite keys cover company, branch, credential and version scope. Account resource bindings are unique per company. Source-order identity is unique by company, connection and external order ID. Triggers prevent changes to published versions, pinned connection identities, immutable mapping revisions and imported source snapshots. Related invoices use the scoped immutable document reference and allocation data. Connector and connection audit events use the existing `SecurityAudits` records. Credential ciphertext is stored independently of connection DTOs. Migration `20261127004608_ConnectorLifecycle` introduces opt-in deprecation and retirement at the connector level. Database constraints keep published version rows immutable throughout either transition.


## Phase 8 mapping, execution and synchronization

Migration `20261127010801_MappingExecutionAndAccountingSync` adds company profiles, mapping revisions and reference mappings, vendor templates, execution policies and sandbox test results, accounting destinations, outbox operation pins, attempt history, external identities and mock account records. It publishes a development accounting version for supported write and readback workflows. Existing connector versions and links keep their original pins. A unique relational association binds one accounting operation to one financial document. The mock accounting store enforces an operation key and scoped document-number uniqueness.

Mapping, template, execution-policy, receipt and evidence records have database immutability guards. A mapping revision can record first activation. A synchronization attempt can record completion once. A synchronization operation can replace the pinned outbound revision only before its first submission. The original attempt remains visible. Destination and connector version fields remain fixed.

Migration `20261127012025_SourceStatusHistoryAndResponsePins` adds scoped append-only store observations and optional pinned response-mapping references. Original source records remain immutable and retain their external identity. A higher provider revision controls subsequent eligibility and source limits. Response mappings share the existing revision relation. An absent response profile uses the fixed normalized development response contract.


## Reporting preparation evidence

Migration `20261127053018_ReportingPreparationGateway` adds ReportingPreparations and ReportingSimulations. Composite foreign keys bind preparation to the same subscriber/company fiscal document and simulation to the same scoped preparation. A unique document/schema key and subscriber row lock make preparation idempotent. A unique preparation simulation key makes local simulation idempotent. PostgreSQL triggers reject updates/deletes to either evidence table and reject preparation inserts for Sales Orders. Original documents and processing states are unchanged. Content is exact text rather than JSONB so its UTF-8 integrity digest is reproducible.

### Regulatory authorization storage

Migration 20261127061232 adds RegulatoryAuthorizations, RegulatoryAuthorizationVersions, RegulatoryAuthorizationFiles and RegulatoryAuthorizationReviews. Composite keys bind every row to its taxpayer company. The register has a composite foreign key to its branch, and its scope is unique by branch, authorization kind and document type. Versions and review events have independent numbered histories. PostgreSQL triggers deny edits and deletes. The subscriber lock and expected numbers protect submission and review against stale clients. The evidence bytes and their SHA-256 metadata are stored separately.

### Provider accreditation storage

Migration 20261127062039 adds AccreditationVersions and AccreditationFiles. The category, record key and version number identify append-only records and their optional evidence. PostgreSQL rejects edits and deletes. Optimistic expected versions and unique keys reject concurrent overwrites. The two dedicated accreditation grants are assigned to existing synthetic vendor publishers and new local vendor administrators.

### Schema storage

Migration 20261127063356 adds RegulatorySchemaVersions and seeds the internal canonical invoice and Development simulation profiles. Each row pins its classification, environment, fiscal document scope, content, SHA-256 checksum and effective dates. PostgreSQL makes versions append-only and only permits disabled, unverified entries in the reserved official classifications. New supported versions require a reviewed application change and additive migration.
